using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Puffinote.Components.Shell;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Document auto-save in the shell: every dirty tab is written back once the editing stops — every one,
/// not just the active one, because a background tab is the work most likely to be lost unnoticed.
/// <para>
/// The shell owns this rather than the editors: in Puffinote each pane's <c>SaveAsync</c> is the editor's
/// own atomic write, and the tab host is what calls it. Tests drive <c>AutoSaveNowForTest</c> rather than
/// waiting — a <c>DispatcherTimer</c> doesn't tick in a headless suite that isn't pumping real time.
/// </para>
/// </summary>
public sealed class AutoSaveTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-autosave");

    public AutoSaveTests()
    {
        // The per-user settings file is shared across the run; start from Puffinote's own defaults so a
        // test that sets an interval can't leak it into the next one.
        if (File.Exists(PuffinoteSettings.UserPath))
        {
            File.Delete(PuffinoteSettings.UserPath);
        }
    }

    public void Dispose()
    {
        try
        {
            _dir.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    private string WriteNote(string name, string text)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllText(path, text);
        return path;
    }

    private MainWindow OpenNotebook()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        window.OpenNotebook(_dir.FullName);
        return window;
    }

    /// <summary>
    /// Leave the active note holding <paramref name="text"/> as an <em>unsaved</em> edit. <c>LoadRecovered</c>
    /// is the editor's public way into a dirty state without driving focus and keystrokes, which headless
    /// tests can't do reliably — the same seam <see cref="MarkdownPaneTests"/> uses. (The <c>Text</c> setter
    /// is no good here: it loads a document and resets the saved baseline, which is the opposite state.)
    /// </summary>
    private static void Edit(MainWindow window, string text)
    {
        Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest).LoadRecovered(text);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The active note's text, read off the editor the shell actually mounted.</summary>
    private static string ActiveText(MainWindow window) =>
        Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest).Text;

    [AvaloniaFact]
    public async Task An_edited_note_is_written_back_without_anyone_pressing_Save()
    {
        var note = WriteNote("note.md", "# original");
        var window = OpenNotebook();
        await window.OpenFileAsync(note);

        Edit(window, "# edited by auto-save");

        var edited = ActiveText(window);
        Assert.Equal("# edited by auto-save", edited);
        Assert.True(window.AutoSavePendingForTest); // the edit armed the countdown

        await window.AutoSaveNowForTest();

        Assert.Equal(edited, File.ReadAllText(note));
        Assert.False(window.AutoSavePendingForTest);
    }

    [AvaloniaFact]
    public async Task A_dirty_background_tab_is_saved_too_not_just_the_one_in_front()
    {
        // The whole reason this runs over every tab: the document nobody is looking at is the one whose
        // unsaved dot nobody notices.
        var first = WriteNote("first.md", "# first");
        var second = WriteNote("second.md", "# second");

        var window = OpenNotebook();
        await window.OpenFileAsync(first);
        Edit(window, "# first, edited");
        var firstText = ActiveText(window);

        await window.OpenFileAsync(second); // pushes `first` into the background, still dirty
        Edit(window, "# second, edited");
        var secondText = ActiveText(window);

        await window.AutoSaveNowForTest();

        Assert.Equal("# first, edited", File.ReadAllText(first)); // the background tab, saved too
        Assert.Equal("# second, edited", File.ReadAllText(second));
        Assert.Equal(firstText, File.ReadAllText(first));
        Assert.Equal(secondText, File.ReadAllText(second));
    }

    [AvaloniaFact]
    public async Task Nothing_is_written_while_a_dialog_is_open()
    {
        // A modal may be the dirty-close prompt, whose answer may be "discard". Writing underneath it
        // would answer it for the user, the wrong way.
        var note = WriteNote("guarded.md", "# on disk");
        var window = OpenNotebook();
        await window.OpenFileAsync(note);
        Edit(window, "# edited but held back");

        window.InvokeShellForTest("shell.preferences");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsOverlayOpenForTest);

        await window.AutoSaveNowForTest();

        Assert.Equal("# on disk", File.ReadAllText(note));
    }

    [AvaloniaFact]
    public async Task Turning_the_interval_off_stops_it()
    {
        new PuffinoteSettings { AutoSaveSeconds = 0 }.SaveUser();

        var note = WriteNote("off.md", "# keep me");
        var window = OpenNotebook();
        await window.OpenFileAsync(note);

        Edit(window, "# should not reach disk");
        Assert.False(window.AutoSavePendingForTest);

        await window.AutoSaveNowForTest();
        Assert.Equal("# keep me", File.ReadAllText(note));
    }

    [Fact]
    public void The_interval_defaults_to_the_family_default_and_a_notebook_can_override_it()
    {
        Assert.Equal(AutoSave.DefaultSeconds, new PuffinoteSettings().AutoSaveSecondsOrDefault);

        // The per-notebook layer wins over the per-user one, like every other setting here.
        var user = new PuffinoteSettings { AutoSaveSeconds = 60 };
        Assert.Equal(120, user.Layer(new PuffinoteSettings { AutoSaveSeconds = 120 }).AutoSaveSecondsOrDefault);

        // …and a notebook that says nothing leaves the user's choice alone.
        Assert.Equal(60, user.Layer(new PuffinoteSettings()).AutoSaveSecondsOrDefault);
    }
}
