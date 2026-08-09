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
        double y = AddRow(view).TranslatePoint(new Point(0, 0), window)!.Value.Y;
        Assert.InRange(y, 0, 600);
    }

    /// <summary>
    /// The add row keeps the keyboard after every Enter, so a run of five tasks is five titles and five
    /// Enters. It used to be a permanent box that surrendered focus on the first one, which made each of
    /// the other four a trip back to the pointer.
    /// </summary>
    [AvaloniaFact]
    public void Adding_a_task_leaves_the_field_open_and_focused_for_the_next_one()
    {
        var (view, document) = Open();
        var group = document.Board.Groups[0];

        AddRow(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        foreach (string title in new[] { "Buy milk", "Call the bank" })
        {
            var field = (TextBox)AddRow(view);
            Assert.True(field.IsFocused);
            field.Text = title;
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(
            ["Rewrite the empty states", "Chase invoice 2231", "Buy milk", "Call the bank"],
            group.Tasks.Select(t => t.Title));
        Assert.Equal(string.Empty, ((TextBox)AddRow(view)).Text ?? string.Empty);
    }

    /// <summary>Escape throws away what is half-typed; it is the only thing that does.</summary>
    [AvaloniaFact]
    public void Escape_closes_the_add_field_without_keeping_what_was_typed()
    {
        var (view, document) = Open();

        AddRow(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var field = (TextBox)AddRow(view);
        field.Text = "never mind";
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<Button>(AddRow(view));
        Assert.Equal(2, document.Board.Groups[0].Tasks.Count);
        Assert.False(document.CanUndo);
    }

    /// <summary>Dragging a card into another column moves the task there, in one undoable step.</summary>
    [AvaloniaFact]
    public void Dragging_a_card_into_another_column_moves_the_task()
    {
        var (view, document) = Open();
        var card = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");
        var target = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t3");

        Drag(card, Centre(target));

        Assert.Equal("g2", document.Board.Locate("t1")!.Value.Group.Id);
        Assert.Equal("Move Task", document.UndoLabel);

        document.Undo();
        Assert.Equal("g1", document.Board.Locate("t1")!.Value.Group.Id);
    }

    /// <summary>
    /// …and dragging within one column reorders it, which the old press/release pair could not do at all:
    /// it only ever moved a card that had changed columns, so "put this one at the top" was impossible.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_a_card_within_its_own_column_reorders_it()
    {
        var (view, document) = Open();
        var second = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t2");
        var first = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");

        // Above the first card's midpoint, which is what "before it" means.
        var above = Centre(first);
        Drag(second, above.WithY(above.Y - (first.Bounds.Height / 2) + 1));

        Assert.Equal(["t2", "t1"], document.Board.Groups[0].Tasks.Select(t => t.Id));
    }

    /// <summary>
    /// A press that never travelled is a click, and a click opens the task. The drag has to cost a few
    /// pixels of movement or it would eat every click on the board.
    /// </summary>
    [AvaloniaFact]
    public void A_press_that_does_not_travel_is_still_a_click()
    {
        var (view, document) = Open();
        var card = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");

        Drag(card, Centre(card).WithX(Centre(card).X + 2));

        Assert.True(view.IsModalOpen);
        Assert.False(document.CanUndo);
    }

    /// <summary>…and a drag that ended somewhere else does not also open the task it moved.</summary>
    [AvaloniaFact]
    public void A_drag_does_not_also_open_the_task_it_moved()
    {
        var (view, _) = Open();
        var card = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");

        Drag(card, Centre(view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t3")));

        Assert.False(view.IsModalOpen);
    }

    private static Point Centre(Visual target)
    {
        var window = (Window)TopLevel.GetTopLevel(target)!;
        return target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
    }

    /// <summary>Press on <paramref name="card"/>, travel to <paramref name="to"/>, and let go there.</summary>
    private static void Drag(TaskCard card, Point to)
    {
        var window = (Window)TopLevel.GetTopLevel(card)!;
        var from = Centre(card);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>The first group's add row, whichever of its two forms it is currently in.</summary>
    private static Avalonia.Controls.Control AddRow(TaskBoardView view) =>
        view.BoardForTest.GetLogicalDescendants().OfType<Avalonia.Controls.Control>()
            .First(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Add a task");

    /// <summary>
    /// Every word on a card has a brush to draw it with.
    ///
    /// <para>The title used to set <c>Foreground</c> to null for a task that wasn't done, meaning "leave
    /// it alone". Foreground is an inherited property, so null is not that — it is a local value that
    /// stops the inheritance and leaves the text layout with no brush, and the title drew nothing. Every
    /// card on the board was a checkbox and a star with a blank between them, which is invisible to a
    /// test that only reads <c>TextBlock.Text</c>.</para>
    /// </summary>
    [AvaloniaFact]
    public void Nothing_on_a_card_is_drawn_with_no_brush()
    {
        var (view, document) = Open();
        document.Execute(BoardCommands.SetDone(document.Board.Groups[0].Tasks[0], true));

        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();
        var cards = view.BoardForTest.CardsForTest.Concat(view.ListForTest.CardsForTest).ToList();
        Assert.NotEmpty(cards);

        foreach (var card in cards)
        {
            foreach (var text in card.GetLogicalDescendants().OfType<TextBlock>())
            {
                Assert.True(text.Foreground is not null, $"“{text.Text}” on {card.Task.Id} has no brush");
            }
        }
    }

    /// <summary>
    /// The checkbox, the title, and the star sit on one line.
    ///
    /// <para>They did not: a stock CheckBox and Button are 32px tall and centre their glyph in that,
    /// while a line of the card's text is 16px and sat at the top of the row — so the title rode six
    /// pixels above the two things beside it. The star was a "★" typed into a button, which is whatever
    /// size the font decides and came out visibly smaller than everything around it.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_checkbox_the_title_and_the_star_share_one_line()
    {
        var (view, document) = Open();

        // A title long enough to wrap in a 280px column, so the rule is checked on a two-line card too.
        document.Execute(BoardCommands.AddTask(
            document.Board.Groups[0],
            new TaskItem { Id = "t4", Title = "Rewrite the onboarding empty states before the review" }));

        var window = (Window)TopLevel.GetTopLevel(view)!;

        static void AssertAligned(IReadOnlyList<TaskCard> cards)
        {
            Assert.NotEmpty(cards);
            foreach (var card in cards)
            {
                double Top(Visual part) => part.TranslatePoint(new Point(0, 0), card)!.Value.Y;

                var check = card.GetLogicalDescendants().OfType<CheckBox>().First();
                var title = card.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == card.Task.Title);

                // By name, not by type: CheckBox derives from Button, so "the first Button on the card"
                // is the tick box, and every assertion below it would have been comparing it to itself.
                var star = card.GetLogicalDescendants().OfType<Button>().First(b =>
                    Avalonia.Automation.AutomationProperties.GetName(b) is "Add star" or "Remove star");

                // Against the title's *first line*, not the middle of the block: a title that wraps grows
                // downward and leaves the checkbox where it was, rather than dragging it into the middle
                // of a paragraph. That is the prototype's rule, and why the line box is pinned at all.
                double line = Top(title) + (title.LineHeight / 2);
                Assert.True(title.Bounds.Height > 0, $"{card.Task.Id} was never laid out");

                Assert.Equal(line, Top(check) + (check.Bounds.Height / 2), precision: 0);
                Assert.Equal(line, Top(star) + (star.Bounds.Height / 2), precision: 0);

                // …and the star is drawn at a stated size rather than at whatever the font felt like.
                var glyph = star.GetLogicalDescendants().OfType<Puffinote.Components.Controls.Icons.LucideIcon>().Single();
                Assert.Equal(StarGlyphSize, glyph.Size);
                Assert.Equal(card.Task.Important, glyph.IsFilled); // solid means starred, not just gold
            }
        }

        // Each view measured while it is the one on screen: the other is detached, and a detached card
        // reports stale bounds that would let this pass on numbers nobody can see.
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AssertAligned(view.BoardForTest.CardsForTest);

        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AssertAligned(view.ListForTest.CardsForTest);
    }

    /// <summary>The star's drawn size, shared by the card and the task dialog's header.</summary>
    private const double StarGlyphSize = 18;

    /// <summary>
    /// Scrolled to the end, the trailing "add a list" is fully on screen. It used to be clipped however
    /// far you scrolled: the inset was the scroller's Padding, which shrinks the viewport without joining
    /// the extent, so the far edge of the content was permanently out of reach by exactly that much.
    /// </summary>
    [AvaloniaFact]
    public void The_trailing_add_a_list_can_be_scrolled_fully_into_view()
    {
        var board = new Board();
        for (int i = 0; i < 4; i++)
        {
            board.Groups.Add(new TaskGroup { Id = $"g{i}", Name = $"Group {i}" });
        }

        var view = new TaskBoardView { Document = new BoardDocument(board), Today = Today };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.BoardForTest.GetLogicalDescendants().OfType<ScrollViewer>()
            .First(s => s.HorizontalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        scroll.Offset = new Vector(scroll.Extent.Width, 0); // clamped to the far end
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var add = view.BoardForTest.GetLogicalDescendants().OfType<Button>()
            .First(b => (b.Content as string)?.Contains("Add a list", StringComparison.Ordinal) == true);
        double right = add.TranslatePoint(new Point(0, 0), scroll)!.Value.X + add.Bounds.Width;

        Assert.True(
            right <= scroll.Viewport.Width,
            $"its right edge is at {right} in a {scroll.Viewport.Width}-wide viewport");
    }

    /// <summary>
    /// Given room, the strip is the prototype's single row: search on the left, filters pushed right, the
    /// view switch at the far end with its words on.
    /// </summary>
    [AvaloniaFact]
    public void The_header_is_one_row_when_there_is_room_for_one()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;
        Resize(window, 1100);

        Assert.False(view.HeaderIsCompactForTest);

        // On one row means sharing a centre line, not a top edge: the search box is taller than a pill.
        Assert.Equal(Centre(Search(view), view), Centre(FilterPill(view), view), precision: 0);
        Assert.Equal(Centre(Search(view), view), Centre(SwitchButton(view), view), precision: 0);
        Assert.True(SwitchLabel(view).IsVisible);
    }

    /// <summary>
    /// Squeezed, it does what the prototype's own media query does rather than letting the three controls
    /// fight over one row and clipping the losers: the switch drops to its icons, and the search takes a
    /// full-width row of its own underneath.
    /// </summary>
    [AvaloniaFact]
    public void A_narrow_header_drops_the_switch_to_icons_and_gives_search_its_own_row()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;
        Resize(window, 560);

        Assert.True(view.HeaderIsCompactForTest);

        // The switch keeps its buttons — and its names, for anyone who can't read an icon — but not its words.
        Assert.False(SwitchLabel(view).IsVisible);
        Assert.Equal("Board view", Avalonia.Automation.AutomationProperties.GetName(SwitchButton(view)));

        // …and the search has moved below the filters, spanning the strip.
        Assert.True(Top(Search(view), view) > Top(FilterPill(view), view));
        Assert.True(Search(view).Bounds.Width > 400, $"search is only {Search(view).Bounds.Width} wide");
    }

    /// <summary>
    /// Resizing back and forth across the threshold settles both ways. A layout that decides its own
    /// arrangement can chatter on the boundary — wrap, which frees room, which unwraps, forever — so the
    /// width it takes to unwrap is measured from the *full-size* switch, not the shrunken one.
    /// </summary>
    [AvaloniaFact]
    public void The_header_settles_on_the_way_back_out_as_well_as_in()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;

        foreach (var (width, compact) in new[] { (1100.0, false), (560.0, true), (1100.0, false), (480.0, true) })
        {
            Resize(window, width);
            Assert.Equal(compact, view.HeaderIsCompactForTest);
        }
    }

    private static void Resize(Window window, double width)
    {
        window.Width = width;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static double Top(Visual part, Visual root) => part.TranslatePoint(new Point(0, 0), root)!.Value.Y;

    private static double Centre(Visual part, Visual root) => Top(part, root) + (part.Bounds.Height / 2);

    private static TextBox Search(TaskBoardView view) =>
        view.GetLogicalDescendants().OfType<TextBox>().First(b => b.PlaceholderText == "Search tasks");

    private static Button FilterPill(TaskBoardView view) =>
        view.GetLogicalDescendants().OfType<Button>().First(b => (b.Content as string) == "All");

    private static Button SwitchButton(TaskBoardView view) =>
        view.GetLogicalDescendants().OfType<Button>()
            .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Board view");

    private static TextBlock SwitchLabel(TaskBoardView view) =>
        SwitchButton(view).GetLogicalDescendants().OfType<TextBlock>().First();

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
