using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Pivotask.Control.Views;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// The board and the list, driven the way a person drives them. Both read the same document and the
/// same filter/search seam, which is the property these exist to hold.
/// </summary>
public class BoardViewTests
{
    private static readonly DateOnly Today = new(2026, 8, 7);

    private static (TaskBoardView View, BoardDocument Document) Open()
    {
        var board = new Board();
        board.Labels.Add(new Pivotask.Core.Model.Label { Id = "design", Name = "design", Color = "#0e7490" });

        var inbox = new TaskGroup { Id = "g1", Name = "Inbox" };
        var week = new TaskGroup { Id = "g2", Name = "This week" };
        inbox.Tasks.Add(new TaskItem { Id = "t1", Title = "Rewrite the empty states", Important = true });
        inbox.Tasks.Add(new TaskItem { Id = "t2", Title = "Chase invoice 2231", Due = Today.AddDays(-2) });
        week.Tasks.Add(new TaskItem { Id = "t3", Title = "Draft the retro agenda", Done = true });
        board.Groups.Add(inbox);
        board.Groups.Add(week);

        var document = new BoardDocument(board);
        var view = new TaskBoardView { Document = document, Today = Today };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (view, document);
    }

    [AvaloniaFact]
    public void The_board_draws_one_column_per_group_and_one_card_per_task()
    {
        var (view, _) = Open();

        Assert.Equal(2, view.BoardForTest.ColumnCountForTest);
        Assert.Equal(3, view.BoardForTest.CardsForTest.Count);
    }

    [AvaloniaFact]
    public void Ticking_a_card_completes_the_task_through_the_undo_stack()
    {
        // Not by mutating the model directly: everything the board does has to be undoable.
        var (view, document) = Open();
        var card = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");

        card.GetLogicalDescendants().OfType<CheckBox>().First().IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(document.Board.Locate("t1")!.Value.Task.Done);
        Assert.True(document.CanUndo);

        document.Undo();
        Assert.False(document.Board.Locate("t1")!.Value.Task.Done);
    }

    [AvaloniaFact]
    public void A_filter_hides_the_cards_it_excludes()
    {
        var (view, _) = Open();

        view.Filter = TaskFilter.Starred;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["t1"], view.BoardForTest.CardsForTest.Select(c => c.Task.Id));

        view.Filter = TaskFilter.Unfinished;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["t1", "t2"], view.BoardForTest.CardsForTest.Select(c => c.Task.Id));
    }

    [AvaloniaFact]
    public void An_overdue_task_is_due_soon_however_late_it_is()
    {
        var (view, _) = Open();
        view.Filter = TaskFilter.DueSoon;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["t2"], view.BoardForTest.CardsForTest.Select(c => c.Task.Id));
    }

    [AvaloniaFact]
    public void The_search_box_narrows_the_board()
    {
        var (view, _) = Open();
        view.Query = "invoice";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["t2"], view.BoardForTest.CardsForTest.Select(c => c.Task.Id));
    }

    [AvaloniaFact]
    public void The_list_view_shows_the_same_tasks_under_the_same_filter()
    {
        // The property Phase 5 rests on: one filter set, two readings of it.
        var (view, _) = Open();
        view.Filter = TaskFilter.Unfinished;
        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, view.ListForTest.RowCountForTest);
    }

    [AvaloniaFact]
    public void A_collapsed_group_shows_no_cards_but_keeps_its_column()
    {
        var (view, document) = Open();
        document.Execute(BoardCommands.SetCollapsed(document.Board.Groups[0], true));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, view.BoardForTest.ColumnCountForTest);
        Assert.Equal(["t3"], view.BoardForTest.CardsForTest.Select(c => c.Task.Id));
    }

    [AvaloniaFact]
    public void Folding_completed_tasks_hides_them_in_that_group_only()
    {
        var (view, document) = Open();
        document.Execute(BoardCommands.SetFoldDone(document.Board.Groups[1], true));
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(view.BoardForTest.CardsForTest, c => c.Task.Id == "t3");
        Assert.Equal(2, view.BoardForTest.CardsForTest.Count);
    }

    [AvaloniaFact]
    public void The_board_repaints_itself_when_the_document_changes_underneath_it()
    {
        // Rebuild-on-change rather than diffing: the view cannot quietly stop agreeing with the model.
        var (view, document) = Open();
        document.Execute(BoardCommands.AddTask(document.Board.Groups[0], new TaskItem { Id = "t4", Title = "new" }));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(view.BoardForTest.CardsForTest, c => c.Task.Id == "t4");
    }

    [AvaloniaFact]
    public void The_catalog_is_built_once_and_reports_its_own_state()
    {
        var (view, document) = Open();
        Assert.Same(view.EditingCommands, view.EditingCommands);

        var undo = view.EditingCommands.Single(c => c.Id == "edit.undo");
        Assert.False(undo.IsEnabled);

        document.Execute(BoardCommands.SetDone(document.Board.Groups[0].Tasks[0], true));
        Assert.True(undo.IsEnabled);

        var list = view.EditingCommands.Single(c => c.Id == "view.list");
        Assert.False(list.IsToggleActive);
        list.Execute();
        Assert.True(list.IsToggleActive);
    }

    [AvaloniaFact]
    public void Adding_a_group_from_the_catalog_puts_a_column_on_the_board()
    {
        var (view, _) = Open();
        view.EditingCommands.Single(c => c.Id == "task.addGroup").Execute();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, view.BoardForTest.ColumnCountForTest);
    }

    [AvaloniaFact]
    public void The_groups_a_host_outlines_are_the_board_s_own()
    {
        var (view, _) = Open();

        Assert.Equal(["Inbox", "This week"], view.Groups.Select(g => g.Name));
    }

    /// <summary>
    /// A single click opens the task, as it does in the prototype. The card is a summary of something
    /// there is more of, and opening it is the only thing a plain click on one could mean.
    /// </summary>
    [AvaloniaFact]
    public void A_single_click_on_a_card_opens_the_task()
    {
        var (view, _) = Open();
        var card = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");

        Click(card);

        Assert.True(view.IsModalOpen);
    }

    /// <summary>
    /// …but a click that landed on the checkbox or the star has already meant something. Those are their
    /// own targets inside the card, and letting the card also act on them would tick a task *and* open it.
    /// </summary>
    [AvaloniaFact]
    public void A_click_on_the_checkbox_does_not_also_open_the_task()
    {
        var (view, _) = Open();
        var card = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");
        var check = card.GetLogicalDescendants().OfType<CheckBox>().First();

        Click(check);

        Assert.Contains(view.BoardForTest.CardsForTest, c => c.Task is { Id: "t1", Done: true }); // it ticked…
        Assert.False(view.IsModalOpen);                                                          // …and only that
    }

    /// <summary>A real click, through the window, so the guard inside the card is actually exercised.</summary>
    private static void Click(Visual target)
    {
        var window = (Window)TopLevel.GetTopLevel(target)!;
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The card's facts read as facts: a step count says what it counts, and a task with a note says so,
    /// since the note is the one thing on a task the card cannot show.
    /// </summary>
    [AvaloniaFact]
    public void A_card_spells_out_its_step_count_and_says_when_there_is_a_note()
    {
        var board = new Board();
        var group = new TaskGroup { Id = "g1", Name = "Inbox" };
        var task = new TaskItem { Id = "t1", Title = "Ship it", Notes = "the tricky part" };
        task.Steps.Add(new TaskStep { Id = "s1", Text = "draft", Done = true });
        task.Steps.Add(new TaskStep { Id = "s2", Text = "review" });
        group.Tasks.Add(task);
        board.Groups.Add(group);

        var view = new TaskBoardView { Document = new BoardDocument(board), Today = Today };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var card = view.BoardForTest.CardsForTest.Single();
        var chips = card.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("1/2 steps", chips);
        Assert.Contains("note", chips);
    }
}
