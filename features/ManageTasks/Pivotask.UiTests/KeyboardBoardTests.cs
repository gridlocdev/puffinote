using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Pivotask.Control.Views;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// Getting around the board, and moving a task, without a mouse. Proposal 0004 §7 is explicit that the
/// keyboard path is the one that must work — drag is the affordance, not the mechanism — and until now
/// there wasn't one: <c>BoardCommands.MoveTask</c> existed and nothing reached it except a drag. A board
/// you can only rearrange by dragging is a board somebody cannot rearrange at all.
/// </summary>
public class KeyboardBoardTests
{
    private static (TaskBoardView View, BoardDocument Document) NewBoard()
    {
        var document = new BoardDocument();
        document.Execute(BoardCommands.AddGroup("To do"));
        document.Execute(BoardCommands.AddGroup("Doing"));

        var todo = document.Board.Groups[0];
        var doing = document.Board.Groups[1];
        document.Execute(BoardCommands.AddTask(todo, new TaskItem { Id = "t1", Title = "first" }));
        document.Execute(BoardCommands.AddTask(todo, new TaskItem { Id = "t2", Title = "second" }));
        document.Execute(BoardCommands.AddTask(doing, new TaskItem { Id = "t3", Title = "other" }));

        var view = new TaskBoardView { Document = document, Layout = BoardLayout.Board };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, document);
    }

    private static TaskCard Card(TaskBoardView view, string title) =>
        view.BoardForTest.CardsForTest.First(c => c.Task.Title == title);

    private static void Press(TaskCard card, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        card.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = card,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static string[] Titles(BoardDocument document, int group) =>
        [.. document.Board.Groups[group].Tasks.Select(t => t.Title)];

    [AvaloniaFact]
    public void Down_moves_the_focus_to_the_next_card_in_the_column()
    {
        var (view, _) = NewBoard();
        var first = Card(view, "first");
        first.Focus();

        Press(first, Key.Down);

        Assert.True(Card(view, "second").IsFocused);
    }

    /// <summary>
    /// Navigating must not be followed by the put-the-focus-back pass a <em>move</em> needs: that pass
    /// re-focuses the card the key came from, which would undo every arrow press the instant it landed.
    /// This is the bug this distinction exists to prevent, so it is the one with a test on it.
    /// </summary>
    [AvaloniaFact]
    public void Navigating_does_not_snap_the_focus_back_to_where_it_started()
    {
        var (view, _) = NewBoard();
        var first = Card(view, "first");
        first.Focus();

        Press(first, Key.Down);
        Dispatcher.UIThread.RunJobs(); // let any posted focus restore run

        Assert.False(first.IsFocused);
        Assert.True(Card(view, "second").IsFocused);
    }

    /// <summary>A column's ends are ends: on a board nobody reads the bottom of one column into the top
    /// of the next, so the key is left unhandled rather than jumping somewhere surprising.</summary>
    [AvaloniaFact]
    public void Down_at_the_bottom_of_a_column_goes_nowhere()
    {
        var (view, _) = NewBoard();
        var second = Card(view, "second");
        second.Focus();

        Press(second, Key.Down);

        Assert.True(second.IsFocused);
    }

    [AvaloniaFact]
    public void Right_moves_the_focus_into_the_next_column()
    {
        var (view, _) = NewBoard();
        var first = Card(view, "first");
        first.Focus();

        Press(first, Key.Right);

        Assert.True(Card(view, "other").IsFocused);
    }

    /// <summary>
    /// ⌥⇧ and an arrow move the task itself — the family's existing idiom, the same one Cellestra's grid
    /// uses on a cell's contents, so the chord is one people have already learned somewhere else.
    /// </summary>
    [AvaloniaFact]
    public void Option_shift_down_swaps_the_task_with_the_one_below_it()
    {
        var (view, document) = NewBoard();
        var first = Card(view, "first");
        first.Focus();

        Press(first, Key.Down, KeyModifiers.Alt | KeyModifiers.Shift);

        Assert.Equal(["second", "first"], Titles(document, 0));
    }

    [AvaloniaFact]
    public void Option_shift_right_moves_the_task_into_the_next_column()
    {
        var (view, document) = NewBoard();
        var first = Card(view, "first");
        first.Focus();

        Press(first, Key.Right, KeyModifiers.Alt | KeyModifiers.Shift);

        Assert.Equal(["second"], Titles(document, 0));
        Assert.Equal(["first", "other"], Titles(document, 1)); // at the same height, not appended
    }

    /// <summary>
    /// The move goes through the same undoable command a drop does, so ⌘Z reverses either identically —
    /// which is the point of routing both through <c>BoardCommands.MoveTask</c> rather than nudging lists.
    /// </summary>
    [AvaloniaFact]
    public void A_keyboard_move_is_one_undoable_step()
    {
        var (view, document) = NewBoard();
        Card(view, "first").Focus();

        Press(Card(view, "first"), Key.Right, KeyModifiers.Alt | KeyModifiers.Shift);
        Assert.Equal(["second"], Titles(document, 0));

        document.Undo();

        Assert.Equal(["first", "second"], Titles(document, 0));
        Assert.Equal(["other"], Titles(document, 1));
    }

    /// <summary>A card at the edge has nowhere further to go, and nothing happens rather than something odd.</summary>
    [AvaloniaFact]
    public void A_move_off_the_edge_of_the_board_is_refused()
    {
        var (view, document) = NewBoard();
        var first = Card(view, "first");
        first.Focus();

        Press(first, Key.Left, KeyModifiers.Alt | KeyModifiers.Shift);

        Assert.Equal(["first", "second"], Titles(document, 0));
    }

    /// <summary>A chord that belongs to the host's catalog is not ours to take.</summary>
    [AvaloniaFact]
    public void A_command_chord_is_left_alone()
    {
        var (view, document) = NewBoard();
        var first = Card(view, "first");
        first.Focus();

        Press(first, Key.Right, KeyModifiers.Meta);

        Assert.Equal(["first", "second"], Titles(document, 0));
        Assert.True(first.IsFocused);
    }
}
