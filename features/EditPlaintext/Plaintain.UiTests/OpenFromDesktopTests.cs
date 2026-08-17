using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Plaintain.Desktop;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// How a file reaches Plaintain from outside it: a launch argument, and — the case this covers — the
/// desktop handing the app a document while it runs. Finder's "Open With" is the second kind on macOS,
/// not the first: a bundled app is launched with an Apple Event and an empty argv, so an association
/// wired only to <c>args[0]</c> opens on "Untitled" and drops the file.
/// </summary>
public class OpenFromDesktopTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"plaintain-openwith-{Guid.NewGuid():N}");

    public OpenFromDesktopTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }

        GC.SuppressFinalize(this);
    }

    private MainWindow Open()
    {
        var window = TestShell.NewWindow(new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json")));
        window.Show();
        return window;
    }

    private string WriteFile(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static void SetText(MainWindow window, string text)
    {
        var buffer = window.EditorView.Editor.Buffer;
        buffer.ReplaceRange(0, buffer.Document.Length, text);
        Dispatcher.UIThread.RunJobs();
    }

    // ---- The arrival ------------------------------------------------------

    /// <summary>The bug: the window is meant to end up showing the file, not an empty buffer.</summary>
    [AvaloniaFact]
    public void A_file_handed_over_by_the_desktop_is_opened()
    {
        var path = WriteFile("server.log", "line one\nline two\n");
        var window = Open();

        window.OpenFromDesktopForTest(path).GetAwaiter().GetResult();

        Assert.Equal(path, window.CurrentPathForTest);
        Assert.Equal("line one\nline two\n", window.EditorView.Text);
    }

    /// <summary>It arrives over a live document, so the unsaved-changes guard applies — unlike argv.</summary>
    [AvaloniaFact]
    public void Unsaved_work_is_not_replaced_without_asking()
    {
        var path = WriteFile("server.log", "line one\n");
        var window = Open();
        SetText(window, "half a thought");

        var opening = window.OpenFromDesktopForTest(path);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("half a thought", window.EditorView.Text); // still waiting on the prompt
        window.DismissDialogForTest();                          // dismissed = "Cancel", i.e. keep the edits
        opening.GetAwaiter().GetResult();

        Assert.Equal("half a thought", window.EditorView.Text);
        Assert.Null(window.CurrentPathForTest);
    }

    /// <summary>
    /// A modal already up owns the screen. At launch that is the crash-recovery prompt, and the overlay
    /// shows one card at a time — so the open waits its turn rather than clearing a question nobody
    /// answered.
    /// </summary>
    [AvaloniaFact]
    public void An_open_dialog_is_waited_out_rather_than_replaced()
    {
        var path = WriteFile("server.log", "line one\n");
        var window = Open();
        window.ShowAbout(); // any modal will do; About is the one with no side effects
        Dispatcher.UIThread.RunJobs();

        var opening = window.OpenFromDesktopForTest(path);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(window.CurrentPathForTest); // held while the About card is up

        window.DismissDialogForTest();
        opening.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(path, window.CurrentPathForTest);
    }

    // ---- Which candidate counts -------------------------------------------

    [AvaloniaFact]
    public void The_first_candidate_is_the_document()
    {
        var first = WriteFile("first.txt", "one");
        var second = WriteFile("second.txt", "two");

        Assert.Equal(first, App.DocumentToOpen([first, second]));
    }

    [AvaloniaFact]
    public void Nothing_to_open_leaves_the_buffer_alone()
    {
        Assert.Null(App.DocumentToOpen([]));
        Assert.Null(App.DocumentToOpen([null]));                          // a file with no local path
        Assert.Null(App.DocumentToOpen([Path.Combine(_dir, "gone.txt")])); // a stale path
        Assert.Null(App.DocumentToOpen([_dir]));                           // a directory is not a document
    }
}
