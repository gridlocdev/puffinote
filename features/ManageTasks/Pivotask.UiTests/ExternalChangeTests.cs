using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Desktop.Services;
using Pivotask.Desktop.Views;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// Something outside Pivotask writes to the open board. Proposal 0004 Phase 4 singles this out: a board
/// file is small, hand-editable JSON, and the single most likely way it changes is arriving with a
/// <c>git pull</c>. Without this, the next save wrote the board as it was when it was opened over
/// whatever the pull brought in.
/// <para>
/// The conflict prompt itself is out of reach of a headless suite — it waits on an answer nobody is there
/// to give — so what is covered here is the decision it delegates to.
/// </para>
/// </summary>
public class ExternalChangeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pivotask-external-{Guid.NewGuid():N}");

    public ExternalChangeTests() => Directory.CreateDirectory(_dir);

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

    private (MainWindow Window, string Path) OpenSavedBoard()
    {
        var path = Path.Combine(_dir, $"board-{Guid.NewGuid():N}.pivotask.json");
        new BoardDocument().Save(path);

        var window = new MainWindow(new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json")));
        window.Show();
        window.OpenPath(path);
        Dispatcher.UIThread.RunJobs();
        return (window, path);
    }

    private static void AddGroup(MainWindow window, string name)
    {
        window.BoardView.Document.Execute(BoardCommands.AddGroup(name));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Rewrite the board the way a pull would, with a stamp that cannot be mistaken for ours.</summary>
    private static void PullInto(string path, string groupName)
    {
        var theirs = new BoardDocument();
        theirs.Execute(BoardCommands.AddGroup(groupName));
        theirs.Save(path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
    }

    [AvaloniaFact]
    public void Opening_a_board_starts_watching_it()
    {
        var (window, path) = OpenSavedBoard();

        Assert.Equal(path, window.WatchedPathForTest);
    }

    /// <summary>With nothing unsaved, the pulled board is the board — showing it is the only useful move.</summary>
    [AvaloniaFact]
    public void A_clean_board_picks_up_what_the_pull_brought_in()
    {
        var (window, path) = OpenSavedBoard();

        PullInto(path, "From the pull");
        window.NoteExternalChangeForTest();

        Assert.Contains(window.BoardView.Document.Board.Groups, g => g.Name == "From the pull");
        Assert.False(window.BoardView.Document.IsDirty);
        Assert.False(window.DiskChangedUnderEditsForTest);
    }

    /// <summary>Unsaved edits are never discarded to settle a conflict nobody has been asked about.</summary>
    [AvaloniaFact]
    public void Unsaved_edits_are_kept_and_the_conflict_is_recorded()
    {
        var (window, path) = OpenSavedBoard();
        AddGroup(window, "Mine");

        PullInto(path, "Theirs");

        Assert.True(window.NoteExternalChangeForTest());
        Assert.True(window.DiskChangedUnderEditsForTest);
        Assert.Contains(window.BoardView.Document.Board.Groups, g => g.Name == "Mine");
        Assert.DoesNotContain(window.BoardView.Document.Board.Groups, g => g.Name == "Theirs");
    }

    /// <summary>
    /// The reason the flag exists: auto-save must not answer "whose version wins?" on a timer. The board
    /// stays dirty and the file keeps what the pull brought until a ⌘S.
    /// </summary>
    [AvaloniaFact]
    public void Auto_save_stands_down_while_a_conflict_is_unresolved()
    {
        var (window, path) = OpenSavedBoard();
        AddGroup(window, "Mine");
        PullInto(path, "Theirs");
        window.NoteExternalChangeForTest();

        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.True(window.BoardView.Document.IsDirty);                                  // edits still unsaved
        Assert.Contains("Theirs", File.ReadAllText(path), StringComparison.Ordinal);     // file still theirs
        Assert.DoesNotContain("Mine", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>Our own save moves the stamp, so the watcher does not report it back to us as a pull.</summary>
    [AvaloniaFact]
    public void Our_own_save_is_not_mistaken_for_somebody_else_s()
    {
        var (window, path) = OpenSavedBoard();
        AddGroup(window, "Mine");
        window.SaveForTest(path);
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.NoteExternalChangeForTest());
        Assert.False(window.DiskChangedUnderEditsForTest);
        Assert.Contains(window.BoardView.Document.Board.Groups, g => g.Name == "Mine");
    }

    /// <summary>A touch that leaves the bytes alone is not a change.</summary>
    [AvaloniaFact]
    public void An_event_with_no_actual_change_does_nothing()
    {
        var (window, _) = OpenSavedBoard();
        AddGroup(window, "Mine");

        Assert.False(window.NoteExternalChangeForTest());
        Assert.False(window.DiskChangedUnderEditsForTest);
    }

    /// <summary>A board that has never been saved has no file to be changed underneath it.</summary>
    [AvaloniaFact]
    public void A_new_board_watches_nothing()
    {
        var window = new MainWindow(new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json")));
        window.Show();

        Assert.Null(window.WatchedPathForTest);
        Assert.False(window.NoteExternalChangeForTest()); // must not throw either
    }
}
