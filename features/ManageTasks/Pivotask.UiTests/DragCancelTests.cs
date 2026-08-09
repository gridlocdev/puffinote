using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Pivotask.Control.Views;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// Getting out of a drag you have thought better of. Everything else Phase 5 asked of drag-and-drop was
/// already there — a line showing exactly where the card would land, the column lighting up, placement
/// between two cards rather than "somewhere in this list" — but there was no way to abandon one. The only
/// exit was to drop the card back precisely where it came from and hope the no-op check caught it, which
/// is a guess rather than an escape hatch, and is not available at all once the pointer has reached
/// another column.
/// </summary>
public class DragCancelTests
{
    private static (TaskBoardView View, BoardDocument Document) NewBoard()
    {
        var document = new BoardDocument();
        document.Execute(BoardCommands.AddGroup("To do"));
        document.Execute(BoardCommands.AddGroup("Doing"));
        document.Execute(BoardCommands.AddTask(document.Board.Groups[0], new TaskItem { Id = "t1", Title = "first" }));

        var view = new TaskBoardView { Document = document };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, document);
    }

    /// <summary>With no drag in flight there is nothing to cancel, and Escape stays available to whatever
    /// else wants it.</summary>
    [AvaloniaFact]
    public void Cancelling_with_no_drag_in_flight_does_nothing()
    {
        var (view, _) = NewBoard();

        Assert.False(view.CancelDrag());
    }

    /// <summary>
    /// The board is left exactly as it was found: nothing executed, so nothing to undo — an undo entry
    /// that reverses nothing is worse than none, because it makes ⌘Z lie about how far back the last real
    /// change was.
    /// </summary>
    [AvaloniaFact]
    public void Cancelling_leaves_the_board_untouched()
    {
        var (view, document) = NewBoard();
        int groups = document.Board.Groups.Count;

        view.CancelDrag();

        Assert.Equal(groups, document.Board.Groups.Count);
        Assert.Equal(["first"], document.Board.Groups[0].Tasks.Select(t => t.Title));

        // Nothing was executed, so the step ⌘Z reaches is still the one before the drag — adding the
        // task. An undo entry that reverses nothing is worse than none: it makes ⌘Z lie about how far
        // back the last real change was.
        document.Undo();
        Assert.Empty(document.Board.Groups[0].Tasks);
    }
}
