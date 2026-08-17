using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Typage.Desktop;
using Typage.Desktop.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// How a file reaches Typage from outside it: a launch argument, and — the case this covers — the desktop
/// handing the app a document while it runs. Finder's "Open With" is the second kind on macOS, not the
/// first: a bundled app is launched with an Apple Event and an empty argv, so an association wired only
/// to <c>args[0]</c> opens on "Untitled" and drops the file. <c>typage &lt;file&gt;</c> arrives the same
/// way once Typage is installed as a bundle, because the CLI reaches it through Launch Services.
///
/// <para>Nothing here blocks on a <see cref="Task"/>: the continuations need the UI thread, so waiting on
/// one that never resolves deadlocks the whole run instead of failing this one case. Pump the dispatcher,
/// then assert the task finished.</para>
/// </summary>
public class OpenFromDesktopTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"typage-openwith-{Guid.NewGuid():N}");

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

    private static MainWindow Open()
    {
        var window = TestShell.NewWindow();
        window.Show();
        return window;
    }

    private string WriteFile(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Run the arrival to completion, failing rather than deadlocking if it never resolves.</summary>
    private static void Settle(Task work)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(work.IsCompleted, "the open never finished — something is still waiting on a dialog");
        work.GetAwaiter().GetResult(); // surface an exception rather than swallowing it
    }

    // ---- The arrival ------------------------------------------------------

    /// <summary>The bug: the window is meant to end up showing the file, not an empty document.</summary>
    [AvaloniaFact]
    public void A_document_handed_over_by_the_desktop_is_opened()
    {
        var path = WriteFile("notes.md", "# Heading\n\nbody\n");
        var window = Open();

        Settle(window.OpenFromDesktopForTest(path));

        Assert.Equal(path, window.EditorView.DocumentPath);
        Assert.Equal("# Heading\n\nbody\n", window.EditorView.Text);
    }

    /// <summary>It arrives over a live document, so the unsaved-changes guard applies — unlike argv.</summary>
    [AvaloniaFact]
    public void Unsaved_work_is_not_replaced_without_asking()
    {
        var path = WriteFile("notes.md", "# Heading\n");
        var window = Open();
        window.EditorView.Editor.Buffer.Insert("half a thought");
        Dispatcher.UIThread.RunJobs();

        var opening = window.OpenFromDesktopForTest(path);
        Dispatcher.UIThread.RunJobs();

        Assert.False(opening.IsCompleted);                      // held at the discard prompt
        Assert.Equal("half a thought", window.EditorView.Text);
        window.DismissDialogForTest();                          // dismissed = "Cancel", i.e. keep the edits
        Settle(opening);

        Assert.Equal("half a thought", window.EditorView.Text);
        Assert.Null(window.EditorView.DocumentPath);
    }

    /// <summary>
    /// A modal already up owns the screen, and the overlay shows one card at a time — so the open waits
    /// its turn rather than clearing a question nobody answered.
    /// </summary>
    [AvaloniaFact]
    public void An_open_dialog_is_waited_out_rather_than_replaced()
    {
        var path = WriteFile("notes.md", "# Heading\n");
        var window = Open();
        window.ShowAbout(); // any modal will do; About is the one with no side effects
        Dispatcher.UIThread.RunJobs();

        var opening = window.OpenFromDesktopForTest(path);
        Dispatcher.UIThread.RunJobs();

        Assert.False(opening.IsCompleted);
        Assert.Null(window.EditorView.DocumentPath); // held while the About card is up

        window.DismissDialogForTest();
        Settle(opening);

        Assert.Equal(path, window.EditorView.DocumentPath);
    }

    // ---- Which candidate counts -------------------------------------------

    [AvaloniaFact]
    public void The_first_candidate_is_the_document()
    {
        var first = WriteFile("first.md", "one");
        var second = WriteFile("second.md", "two");

        Assert.Equal(first, App.DocumentToOpen([first, second]));
    }

    [AvaloniaFact]
    public void Nothing_to_open_leaves_the_document_alone()
    {
        Assert.Null(App.DocumentToOpen(null));                            // no argv at all
        Assert.Null(App.DocumentToOpen([]));
        Assert.Null(App.DocumentToOpen([null]));                          // a file with no local path
        Assert.Null(App.DocumentToOpen([Path.Combine(_dir, "gone.md")])); // a stale path
        Assert.Null(App.DocumentToOpen([_dir]));                          // a directory is not a document
    }
}
