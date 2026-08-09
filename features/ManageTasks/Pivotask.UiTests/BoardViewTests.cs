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

    /// <summary>
    /// Collapsing is the <em>list's</em> affordance, as in the prototype: a section folds away, a board
    /// column does not. Columns already sit side by side, so folding one buys nothing and costs the
    /// glanceable shape of the board.
    /// </summary>
    [AvaloniaFact]
    public void A_collapsed_group_folds_in_the_list_and_is_ignored_on_the_board()
    {
        var (view, document) = Open();
        document.Execute(BoardCommands.SetCollapsed(document.Board.Groups[0], true));
        Dispatcher.UIThread.RunJobs();

        // The board keeps every column and every card.
        Assert.Equal(2, view.BoardForTest.ColumnCountForTest);
        Assert.Equal(["t1", "t2", "t3"], view.BoardForTest.CardsForTest.Select(c => c.Task.Id));

        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();

        // The list drops the folded section's cards but keeps its heading, so you can unfold it again.
        Assert.Equal(["t3"], view.ListForTest.CardsForTest.Select(c => c.Task.Id));
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

    /// <summary>
    /// The list renders the same card the board does, so a task carries the same facts in both views.
    /// It used to render a thinner row of its own — checkbox, title, star, due — so a task with labels and
    /// steps read as two different tasks depending on which view you were in.
    /// </summary>
    [AvaloniaFact]
    public void A_task_carries_the_same_facts_in_the_list_as_on_the_board()
    {
        var board = new Board();
        board.Labels.Add(new Pivotask.Core.Model.Label { Id = "ops", Name = "ops", Color = "#4d7c0f" });
        var group = new TaskGroup { Id = "g1", Name = "Inbox" };
        var task = new TaskItem { Id = "t1", Title = "Draft the retro", Notes = "with the team" };
        task.Labels.Add("ops");
        task.Steps.Add(new TaskStep { Id = "s1", Text = "outline", Done = true });
        task.Steps.Add(new TaskStep { Id = "s2", Text = "send" });
        group.Tasks.Add(task);
        board.Groups.Add(group);

        var view = new TaskBoardView { Document = new BoardDocument(board), Today = Today };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        static List<string?> Facts(TaskCard card) =>
            [.. card.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)];

        var onBoard = Facts(view.BoardForTest.CardsForTest.Single());

        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();
        var inList = Facts(view.ListForTest.CardsForTest.Single());

        Assert.Equal(onBoard, inList);
        Assert.Contains("ops", inList);
        Assert.Contains("1/2 steps", inList);
        Assert.Contains("note", inList);
    }

    /// <summary>
    /// Finished tasks sink under a fold of their own in the list. In one column they would otherwise push
    /// the work you still have off the screen.
    /// </summary>
    [AvaloniaFact]
    public void The_list_folds_finished_tasks_under_a_subhead()
    {
        var (view, document) = Open();
        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(view.ListForTest.CardsForTest, c => c.Task.Id == "t3"); // done, but shown

        document.Execute(BoardCommands.SetFoldDone(document.Board.Groups[1], true));
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(view.ListForTest.CardsForTest, c => c.Task.Id == "t3");
        Assert.Contains(
            view.ListForTest.GetLogicalDescendants().OfType<Button>(),
            b => (b.Content as string)?.Contains("finished · 1", StringComparison.Ordinal) == true);
    }

    /// <summary>A group can be renamed where it sits — the prototype's editable heading.</summary>
    [AvaloniaFact]
    public void Clicking_a_group_name_renames_it_in_place()
    {
        var (view, document) = Open();
        var heading = view.BoardForTest.GetLogicalDescendants().OfType<TextBlock>()
            .First(t => t.Text == "INBOX");

        heading.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
            heading, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
            heading, default, 0, new Avalonia.Input.PointerPointProperties(default, Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
            Avalonia.Input.KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();

        var box = view.BoardForTest.GetLogicalDescendants().OfType<TextBox>()
            .First(b => b.Text == "Inbox");
        box.Text = "Triage";
        box.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Triage", document.Board.Groups[0].Name);
        Assert.True(document.CanUndo); // renaming is an edit like any other
    }

    /// <summary>
    /// Deleting a group with work in it asks first, and the last group is never simply removed — a board
    /// with no groups has nowhere to put anything.
    /// </summary>
    [AvaloniaFact]
    public void Deleting_a_group_that_holds_tasks_asks_first()
    {
        var (view, document) = Open();

        // Not awaited: the answer arrives from the overlay, and what is being asserted is that the
        // question was put at all rather than two tasks quietly disappearing.
        _ = view.DeleteGroupAsync(document.Board.Groups[0]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.IsModalOpen);
        Assert.Equal(2, document.Board.Groups.Count); // nothing gone while the question is open
    }

    /// <summary>
    /// An empty group goes without a question — it is a click to remake — and emptying the board leaves
    /// one fresh group behind rather than nothing at all, which would be a board with nowhere to put a task.
    /// </summary>
    [AvaloniaFact]
    public async Task An_empty_group_deletes_without_asking_and_the_last_one_is_replaced()
    {
        var (view, document) = Open();
        document.Execute(BoardCommands.AddGroup("Scratch", "g3"));

        await view.DeleteGroupAsync(document.Board.Groups.Single(g => g.Id == "g3"));
        Assert.False(view.IsModalOpen);
        Assert.Equal(2, document.Board.Groups.Count);

        foreach (var group in document.Board.Groups.ToList())
        {
            group.Tasks.Clear();
            await view.DeleteGroupAsync(group);
        }

        Assert.Single(document.Board.Groups);
        Assert.Empty(document.Board.Groups[0].Tasks);
    }

    /// <summary>
    /// V flips the view and / jumps to the search box — the prototype's two unmodified keys. Both are
    /// suppressed while you are typing, because a bare letter has to stay a letter where one is expected.
    /// </summary>
    [AvaloniaFact]
    public void V_flips_the_view_and_slash_jumps_to_search_unless_you_are_typing()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;

        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(BoardLayout.List, view.Layout);

        window.KeyPressQwerty(PhysicalKey.Slash, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var search = view.GetLogicalDescendants().OfType<TextBox>().First(b => b.PlaceholderText == "Search tasks");
        Assert.True(search.IsFocused);

        // Now that the caret is in the search box, "v" is a letter someone is typing.
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(BoardLayout.List, view.Layout);
    }

    /// <summary>
    /// The view switch is a segmented pair with the current one lit, not one button that leaves you to
    /// work out which view you are in.
    /// </summary>
    [AvaloniaFact]
    public void The_view_switch_offers_both_views_and_lights_the_one_in_force()
    {
        var (view, _) = Open();

        static Button Named(TaskBoardView v, string name) =>
            v.GetLogicalDescendants().OfType<Button>()
                .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == name);

        Assert.Equal("selected", Avalonia.Automation.AutomationProperties.GetHelpText(Named(view, "Board view")));
        Assert.Null(Avalonia.Automation.AutomationProperties.GetHelpText(Named(view, "List view")));

        Named(view, "List view").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(BoardLayout.List, view.Layout);
        Assert.Equal("selected", Avalonia.Automation.AutomationProperties.GetHelpText(Named(view, "List view")));
    }

    /// <summary>
    /// A column taller than the board scrolls inside itself instead of running off the bottom edge. It
    /// used to simply grow: a group of thirty tasks put its later cards — and the "add a task" row under
    /// them — hundreds of pixels below the window, so a task you added was filed somewhere you could not
    /// look at, which reads as the task never having been added at all.
    /// </summary>
    [AvaloniaFact]
    public void A_column_taller_than_the_board_scrolls_instead_of_running_off_the_bottom()
    {
        var board = new Board();
        var group = new TaskGroup { Id = "g1", Name = "Inbox" };
        for (int i = 0; i < 30; i++)
        {
            group.Tasks.Add(new TaskItem { Id = $"t{i}", Title = $"Task number {i}" });
        }

        board.Groups.Add(group);

        var view = new TaskBoardView { Document = new BoardDocument(board), Today = Today };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var column = view.BoardForTest.GetLogicalDescendants().OfType<Border>().First(b => b.Width == 280);
        Assert.True(
            column.Bounds.Height <= view.BoardForTest.Bounds.Height,
            $"the column is {column.Bounds.Height} tall inside a {view.BoardForTest.Bounds.Height} board");

        // …and the row you type the next task into is still on the screen.
        var add = view.BoardForTest.GetLogicalDescendants().OfType<Avalonia.Controls.Control>()
            .First(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Add a task");
        double y = add.TranslatePoint(new Point(0, 0), window)!.Value.Y;
        Assert.InRange(y, 0, 600);
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
