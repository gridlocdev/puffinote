using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The title bar's "Saved" flash. ⌘S is the one command in the shell whose whole point is that nothing
/// happens: no dialog, no visible change, and the only acknowledgement was the tab's dirty dot going away
/// — which you had to be watching the tab strip to see. Cellestra's standalone window answered this a
/// while ago; the open question (docs/TODO) was whether a notebook tab wants the same answer. It does,
/// and for every pane type, because ⌘S in the shell means the same thing whichever editor is behind it.
/// </summary>
public sealed class SavedBadgeTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-saved-badge");

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

    private async Task<MainWindow> OpenNoteAsync(string name = "notes.md")
    {
        var path = Path.Combine(_dir.FullName, name);
        await File.WriteAllTextAsync(path, "# hello");

        var window = new MainWindow(new NotebookService());
        window.Show();
        window.OpenNotebook(_dir.FullName);
        await window.OpenFileForTest(path);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public async Task Saving_flashes_Saved_in_the_title_bar()
    {
        var window = await OpenNoteAsync();
        Assert.Null(window.TitleBarForTest.ContentForTest.Badge); // nothing yet

        await window.SaveCurrentForTest();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("— Saved", window.TitleBarForTest.ContentForTest.Badge); // the bar draws the dash
    }

    /// <summary>It is a notice, not a label: it goes away on its own, before it could be read as part of
    /// the document's name.</summary>
    [AvaloniaFact]
    public async Task The_badge_clears_itself()
    {
        var window = await OpenNoteAsync();
        await window.SaveCurrentForTest();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("— Saved", window.TitleBarForTest.ContentForTest.Badge); // the bar draws the dash

        window.ExpireSavedBadgeForTest();

        Assert.Null(window.TitleBarForTest.ContentForTest.Badge);
    }

    /// <summary>
    /// A ● and a "Saved" say opposite things, so they never appear together. Typing after a save brings
    /// the dot back and takes the badge with it — by then the save it refers to is out of date.
    /// </summary>
    [AvaloniaFact]
    public async Task An_edit_after_the_save_takes_the_badge_down()
    {
        var window = await OpenNoteAsync();
        await window.SaveCurrentForTest();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("— Saved", window.TitleBarForTest.ContentForTest.Badge); // the bar draws the dash

        // The same seam Puffinote's auto-save tests use to make a note dirty.
        Assert.IsType<Typage.Control.Views.MarkdownEditorView>(window.ActiveEditorViewForTest)
            .LoadRecovered("# hello, edited");
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.TitleBarForTest.ContentForTest.Dot);
        Assert.Null(window.TitleBarForTest.ContentForTest.Badge);
    }
}
