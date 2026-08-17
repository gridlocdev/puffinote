using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Desktop;
using Pivotask.Desktop.Services;
using Pivotask.Desktop.Views;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// How a board reaches Pivotask from outside it: a launch argument, and — the case this covers — the
/// desktop handing the app a board while it runs. Finder's "Open With" is the second kind on macOS, not
/// the first: a bundled app is launched with an Apple Event and an empty argv, so a claim wired only to
/// <c>args[0]</c> opens on an empty board and drops the file. <c>pivotask &lt;board&gt;</c> arrives the
/// same way once Pivotask is installed as a bundle, because the CLI reaches it through Launch Services.
///
/// <para><b>Every window here starts on a <i>saved</i> board.</b> A never-saved
/// <see cref="BoardDocument"/> compares dirty by construction — its starter groups have never been
/// written anywhere — so a window straight from the constructor raises the discard prompt on any open,
/// and a headless run has nobody to answer it. That is the app behaving correctly and the test setup
/// being wrong, and it hangs the whole suite rather than failing this one case.</para>
///
/// <para>Nothing here blocks on a <see cref="Task"/> either: the continuations need the UI thread, so a
/// wait for one that never resolves deadlocks instead of failing. Pump the dispatcher, then assert the
/// task finished.</para>
/// </summary>
public class OpenFromDesktopTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pivotask-openwith-{Guid.NewGuid():N}");

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

    private string WriteBoard(string name)
    {
        var path = Path.Combine(_dir, name);
        new BoardDocument().Save(path);
        return path;
    }

    /// <summary>A window already on a saved board — i.e. with nothing unsaved to be asked about.</summary>
    private (MainWindow Window, string Path) OpenOnSavedBoard()
    {
        var path = WriteBoard($"open-{Guid.NewGuid():N}.pivotask");
        var window = new MainWindow(new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json")));
        window.Show();
        window.OpenPath(path);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.BoardView.Document.IsDirty); // the premise this file rests on
        return (window, path);
    }

    /// <summary>Run the arrival to completion, failing rather than deadlocking if it never resolves.</summary>
    private static void Settle(Task work)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(work.IsCompleted, "the open never finished — something is still waiting on a dialog");
        work.GetAwaiter().GetResult(); // surface an exception rather than swallowing it
    }

    // ---- The arrival ------------------------------------------------------

    /// <summary>The bug: the window is meant to end up showing the board handed over, not the old one.</summary>
    [AvaloniaFact]
    public void A_board_handed_over_by_the_desktop_is_opened()
    {
        var (window, _) = OpenOnSavedBoard();
        var handed = WriteBoard("handed-over.pivotask");

        Settle(window.OpenFromDesktopForTest(handed));

        Assert.Equal(handed, window.BoardView.Document.Path);
    }

    /// <summary>It arrives over a live board, so the unsaved-changes guard applies — unlike argv.</summary>
    [AvaloniaFact]
    public void Unsaved_work_is_not_replaced_without_asking()
    {
        var (window, original) = OpenOnSavedBoard();
        window.BoardView.Document.Execute(BoardCommands.AddGroup("half a thought"));
        Dispatcher.UIThread.RunJobs();

        var handed = WriteBoard("handed-over.pivotask");
        var opening = window.OpenFromDesktopForTest(handed);
        Dispatcher.UIThread.RunJobs();

        Assert.False(opening.IsCompleted); // held at the discard prompt
        window.DismissDialogForTest();     // dismissed = "Cancel", i.e. keep the edits
        Settle(opening);

        Assert.Equal(original, window.BoardView.Document.Path);
        Assert.Contains(window.BoardView.Document.Board.Groups, g => g.Name == "half a thought");
    }

    /// <summary>
    /// A modal already up owns the screen, and the overlay shows one card at a time — so the open waits
    /// its turn rather than clearing a question nobody answered.
    /// </summary>
    [AvaloniaFact]
    public void An_open_dialog_is_waited_out_rather_than_replaced()
    {
        var (window, original) = OpenOnSavedBoard();
        window.ShowAbout(); // any modal will do; About is the one with no side effects
        Dispatcher.UIThread.RunJobs();

        var handed = WriteBoard("handed-over.pivotask");
        var opening = window.OpenFromDesktopForTest(handed);
        Dispatcher.UIThread.RunJobs();

        Assert.False(opening.IsCompleted);
        Assert.Equal(original, window.BoardView.Document.Path); // held while the About card is up

        window.DismissDialogForTest();
        Settle(opening);

        Assert.Equal(handed, window.BoardView.Document.Path);
    }

    // ---- Which candidate counts -------------------------------------------

    [AvaloniaFact]
    public void The_first_candidate_is_the_board()
    {
        var first = WriteBoard("first.pivotask");
        var second = WriteBoard("second.pivotask");

        Assert.Equal(first, App.DocumentToOpen([first, second]));
    }

    [AvaloniaFact]
    public void Nothing_to_open_leaves_the_board_alone()
    {
        Assert.Null(App.DocumentToOpen(null));                                  // no argv at all
        Assert.Null(App.DocumentToOpen([]));
        Assert.Null(App.DocumentToOpen([null]));                                // a file with no local path
        Assert.Null(App.DocumentToOpen([Path.Combine(_dir, "gone.pivotask")])); // a stale path
        Assert.Null(App.DocumentToOpen([_dir]));                                // a directory is not a board
    }
}
