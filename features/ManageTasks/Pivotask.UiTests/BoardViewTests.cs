using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
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

        // Columns, said out loud: a board opens in the *list* now, and the tests below that drive columns
        // have to ask for them rather than inherit them. The other view is detached, and a detached card
        // reports no bounds at all.
        var view = new TaskBoardView { Document = document, Today = Today, Layout = BoardLayout.Board };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (view, document);
    }

    /// <summary>
    /// A board nobody has expressed an opinion about opens as rows.
    ///
    /// <para>Columns are the better picture of a board you already know the shape of; rows are the better
    /// reading of one you don't — which every board is on the day it is made. Wanting columns is said
    /// once, in Preferences or with a press of V, and the board file remembers it from then on.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_board_nobody_has_an_opinion_about_opens_as_rows()
    {
        Assert.Equal(BoardLayout.List, new TaskBoardView().Layout);
    }

    /// <summary>
    /// A board that names a reading opens in it, whatever this machine's preference says.
    ///
    /// <para>Which reading suits a board is a fact about the board — twelve wide columns want one thing,
    /// three checklists another — so it belongs with the board rather than with whoever opened it last.
    /// The preference is what answers for a board that has never said.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_board_that_names_a_reading_opens_in_it()
    {
        var board = new Board { View = BoardLayout.Board };
        board.Groups.Add(new TaskGroup { Id = "g1", Name = "Inbox" });

        // The host has already pushed its own preference in, as a host does before it loads anything.
        var view = new TaskBoardView { Layout = BoardLayout.List };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();

        view.Document = new BoardDocument(board);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(BoardLayout.Board, view.Layout);
    }

    /// <summary>
    /// Switching the view writes the choice into the board — undoably, like every other change to one —
    /// while a host applying a setting leaves no trace.
    ///
    /// <para>That difference is the whole design. Pushing "columns" at a board because this machine's
    /// <c>Open in</c> says so would make an untitled board dirty before anyone had typed into it, and
    /// would overwrite the choice a different board had already recorded.</para>
    /// </summary>
    [AvaloniaFact]
    public void Choosing_a_view_is_recorded_in_the_board_and_applying_a_setting_is_not()
    {
        var (view, document) = Open();
        Assert.Null(document.Board.View);   // opened as a board by the host, not by a choice
        Assert.False(document.CanUndo);

        // A host restoring a state or applying a preference.
        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(document.Board.View);
        Assert.False(document.CanUndo);

        // A person pressing the switch.
        view.GetLogicalDescendants().OfType<Button>()
            .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Board view")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(BoardLayout.Board, view.Layout);
        Assert.Equal(BoardLayout.Board, document.Board.View);
        Assert.Equal("Change View", document.UndoLabel);

        // …and undoing it takes the view back with it, rather than leaving the board saying one thing
        // and showing another.
        document.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(document.Board.View);
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

    /// <summary>Both due filters open backwards without limit: an overdue task is the most urgent thing
    /// either could name.</summary>
    [AvaloniaTheory]
    [InlineData(TaskFilter.DueTomorrow)]
    [InlineData(TaskFilter.DueThisWeek)]
    public void An_overdue_task_is_due_however_late_it_is(TaskFilter filter)
    {
        var (view, _) = Open();
        view.Filter = filter;
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

        var view = new TaskBoardView { Document = new BoardDocument(board), Today = Today, Layout = BoardLayout.Board };
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
    /// Finished tasks sink to the foot of their group, in both readings — crossed out, still readable,
    /// and out of the queue.
    ///
    /// <para>The list used to gather them under a foldable "finished · 3" subhead. That was three pieces
    /// of furniture answering a question the strikethrough already answers: a ticked task is plainly
    /// ticked wherever it sits, so a band announcing that the ticked ones are below it is a label on a
    /// label. The sinking is the part that was doing the work.</para>
    ///
    /// <para>Ordered in the view, not in the file: ticking a task stays one small undoable edit, and
    /// unticking it puts it back where it was rather than at the end.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_finished_task_sinks_to_the_foot_of_its_group_without_a_heading_over_it()
    {
        var (view, document) = Open();
        var group = document.Board.Groups[1];
        document.Execute(BoardCommands.AddTask(group, new TaskItem { Id = "t4", Title = "Book the room" }));
        Dispatcher.UIThread.RunJobs();

        // t3 is done and was added first, so in the file it is still ahead of t4…
        Assert.Equal(["t3", "t4"], group.Tasks.Select(t => t.Id));

        // …and on screen it is behind it, in both views.
        Assert.Equal(["t1", "t2", "t4", "t3"], view.BoardForTest.CardsForTest.Select(c => c.Task.Id));

        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["t1", "t2", "t4", "t3"], view.ListForTest.CardsForTest.Select(c => c.Task.Id));

        // Nothing announces the sinking — no subhead, no rule, no count.
        Assert.DoesNotContain(
            view.ListForTest.GetLogicalDescendants().OfType<Button>(),
            b => (b.Content as string)?.Contains("finished", StringComparison.OrdinalIgnoreCase) == true);

        // Unticking puts it back where the file always had it.
        document.Execute(BoardCommands.SetDone(group.Tasks.Single(t => t.Id == "t3"), false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["t1", "t2", "t3", "t4"], view.ListForTest.CardsForTest.Select(c => c.Task.Id));
    }

    /// <summary>A group whose completed tasks are folded away hides them in both readings — the state is
    /// the file's (<c>foldDone</c>) and the CLI's, and the views go on honouring it.</summary>
    [AvaloniaFact]
    public void Folding_completed_tasks_hides_them_in_the_list_too()
    {
        var (view, document) = Open();
        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(view.ListForTest.CardsForTest, c => c.Task.Id == "t3"); // done, but shown

        document.Execute(BoardCommands.SetFoldDone(document.Board.Groups[1], true));
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(view.ListForTest.CardsForTest, c => c.Task.Id == "t3");
    }

    /// <summary>
    /// A group can be renamed where it sits — the prototype's editable heading.
    ///
    /// <para>A whole click, press and release both: a press on a heading now has two possible meanings —
    /// rename this group, or move it — and which one it was is not known until the pointer either travels
    /// or doesn't.</para>
    ///
    /// <para>Raised on the label rather than clicked through the window, as it always has been: the
    /// headless run draws nothing, so a bare <see cref="TextBlock"/> is not something a pointer can land
    /// on. What a real click lands on is the heading row, which the drag owns and the tests below drive.</para>
    /// </summary>
    [AvaloniaFact]
    public void Clicking_a_group_name_renames_it_in_place()
    {
        var (view, document) = Open();
        var heading = view.BoardForTest.GetLogicalDescendants().OfType<TextBlock>()
            .First(t => t.Text == "INBOX");

        ClickWithoutTravelling(heading);

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
    /// V flips the view and / reaches the search box — the prototype's two unmodified keys. Both are
    /// suppressed while you are typing, because a bare letter has to stay a letter where one is expected.
    ///
    /// <para>The search box now lives in the filter panel, so / has a panel to open before it has a caret
    /// to place.</para>
    /// </summary>
    [AvaloniaFact]
    public void V_flips_the_view_and_slash_reaches_the_search_box_unless_you_are_typing()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;

        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(BoardLayout.List, view.Layout);

        window.KeyPressQwerty(PhysicalKey.Slash, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FilterMenu.IsOpen);
        Assert.True(Search(view).IsFocused);

        view.FilterMenu.Hide();
        view.Layout = BoardLayout.Board; // back to the columns, where the add-a-task row under test lives
        Dispatcher.UIThread.RunJobs();

        // Typing into a field on the board itself — where "v" is a letter, not a command.
        AddRow(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(((TextBox)AddRow(view)).IsFocused);

        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(BoardLayout.Board, view.Layout);
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

        var view = new TaskBoardView { Document = new BoardDocument(board), Today = Today, Layout = BoardLayout.Board };
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

    /// <summary>
    /// The card being dragged is drawn in the air, under the cursor.
    ///
    /// <para>A drag used to show a dimmed gap and a two-pixel line and nothing else — the card itself
    /// stayed exactly where it was, so the gesture read as "nothing is happening" right up until the drop,
    /// and <em>what</em> was being moved was something you had to remember rather than something you could
    /// see. It is a real card built from the same task, so the thing in the air carries the same chips and
    /// the same due pill as the thing being moved.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_dragged_card_lifts_onto_the_cursor_and_is_put_down_on_release()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;
        var card = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");
        var onto = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t3");

        static List<TaskCard> Ghosts(Visual surface) =>
            [.. surface.GetLogicalDescendants().OfType<Canvas>().SelectMany(c => c.Children.OfType<TaskCard>())];

        Assert.Empty(Ghosts(view.BoardForTest)); // nothing in the air until the gesture becomes a drag

        var to = Centre(onto);
        window.MouseDown(Centre(card), MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();

        var ghost = Assert.Single(Ghosts(view.BoardForTest));
        Assert.Same(card.Task, ghost.Task);              // the card in the air is the card being moved
        Assert.Equal(0.35, card.Opacity);                // …and the gap it came from is still there
        Assert.Equal(card.Bounds.Width, ghost.Width);
        Assert.False(ghost.IsHitTestVisible);            // it must not answer the drag it is drawn for
        Assert.IsType<RotateTransform>(ghost.RenderTransform);
        Assert.NotEqual(default, ghost.BoxShadow);

        // Faint enough to see the drop marker through. The card in the air is drawn above everything and
        // spends most of the drag directly on top of the one thing that answers "where exactly?" — at
        // nine-tenths opaque it simply blotted that out, so the drag showed you what you were carrying at
        // the cost of where you were putting it.
        Assert.InRange(ghost.Opacity, 0.2, 0.5);

        // It hangs off the pointer at the point of the card it was picked up by — the middle, here —
        // rather than snapping its own corner to the cursor.
        var pointer = onto.TranslatePoint(
            new Point(onto.Bounds.Width / 2, onto.Bounds.Height / 2), view.BoardForTest)!.Value;
        Assert.Equal(pointer.X - (card.Bounds.Width / 2), Canvas.GetLeft(ghost), precision: 0);
        Assert.Equal(pointer.Y - (card.Bounds.Height / 2), Canvas.GetTop(ghost), precision: 0);

        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(Ghosts(view.BoardForTest));
    }

    /// <summary>
    /// The list is the quieter reading of the two: its tasks carry no outline, and the section you are
    /// dropping into does not light up.
    ///
    /// <para>Both were the board's chrome applied where it doesn't fit. On the board a card's edge is what
    /// separates it from the column it lies in, and a column is a bordered box whose border can change
    /// colour; in the list every row is a task and a section has no border of its own, so the same code
    /// drew thirty redundant outlines and <em>grew</em> a line around a whole screen of tasks under the
    /// pointer. The insertion marker still says exactly where the card is going.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_list_draws_flat_cards_and_never_lights_the_section_you_drop_into()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;

        // The board keeps its outlines: there, the edge is doing work.
        Assert.All(view.BoardForTest.CardsForTest, c => Assert.Equal(new Thickness(1), c.BorderThickness));

        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.NotEmpty(view.ListForTest.CardsForTest);
        Assert.All(view.ListForTest.CardsForTest, c => Assert.Equal(default, c.BorderThickness));

        // Mid-drag — press, travel, and look before letting go.
        var card = view.ListForTest.CardsForTest.First(c => c.Task.Id == "t1");
        var onto = view.ListForTest.CardsForTest.First(c => c.Task.Id == "t3");
        window.MouseDown(Centre(card), MouseButton.Left);
        window.MouseMove(Centre(onto));
        Dispatcher.UIThread.RunJobs();

        var sections = view.ListForTest.GetLogicalDescendants().OfType<Border>()
            .Where(b => b.Margin.Bottom == 14).ToList();
        Assert.NotEmpty(sections);
        Assert.All(sections, s => Assert.Equal(default, s.BorderThickness));

        // …and the marker that answers "where exactly?" is drawn all the same.
        Assert.Contains(
            view.ListForTest.GetLogicalDescendants().OfType<Border>(),
            b => b.Height == 2 && ReferenceEquals(b.Background, Puffinote.Components.Theming.Tokens.Active.Accent));

        window.MouseUp(Centre(onto), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Dragging a group's heading moves the whole group, in one undoable step — columns left and right on
    /// the board, sections up and down in the list.
    ///
    /// <para>The order of the columns used to be the order they had been created in, permanently: putting
    /// "This week" ahead of "Inbox" meant renaming both, or moving every task in one of them. A board's
    /// columns are usually a sequence — a pipeline, a week, a set of stages — and a sequence you cannot
    /// reorder is one you have to get right the first time.</para>
    /// </summary>
    [AvaloniaFact]
    public void Dragging_a_group_heading_moves_the_whole_group()
    {
        var (view, document) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;

        var second = Heading(view.BoardForTest, "THIS WEEK");
        var first = Heading(view.BoardForTest, "INBOX");

        // Left of the first column's midpoint, which is what "before it" means when they run sideways.
        var target = Centre(first);
        window.MouseDown(Centre(second), MouseButton.Left);
        window.MouseMove(target.WithX(target.X - (first.Bounds.Width / 2) - 20));
        Dispatcher.UIThread.RunJobs();

        // Mid-drag: the column is dimmed where it stands and its name is in the air.
        var column = view.BoardForTest.GetLogicalDescendants().OfType<Border>()
            .First(b => b.Width == 280 && b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "THIS WEEK"));
        Assert.Equal(0.35, column.Opacity);
        Assert.Contains(
            view.BoardForTest.GetLogicalDescendants().OfType<Canvas>().SelectMany(c => c.Children.OfType<Border>()),
            g => g.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "THIS WEEK"));

        window.MouseUp(target.WithX(target.X - (first.Bounds.Width / 2) - 20), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["g2", "g1"], document.Board.Groups.Select(g => g.Id));
        Assert.Equal("Move Group", document.UndoLabel);

        document.Undo();
        Assert.Equal(["g1", "g2"], document.Board.Groups.Select(g => g.Id));
    }

    /// <summary>…and the list reorders its sections the same way, running the other direction.</summary>
    [AvaloniaFact]
    public void Dragging_a_section_heading_reorders_the_list()
    {
        var (view, document) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;
        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var second = Heading(view.ListForTest, "THIS WEEK");
        var first = Heading(view.ListForTest, "INBOX");

        var target = Centre(first);
        Drag(second, target.WithY(target.Y - (first.Bounds.Height / 2) - 10));

        Assert.Equal(["g2", "g1"], document.Board.Groups.Select(g => g.Id));
    }

    /// <summary>A group dropped back where it came from is not an edit — an undo entry that reverses
    /// nothing makes ⌘Z lie about how far back the last real change was.</summary>
    [AvaloniaFact]
    public void Dropping_a_group_where_it_already_was_is_not_an_edit()
    {
        var (view, document) = Open();
        var heading = Heading(view.BoardForTest, "INBOX");

        Drag(heading, Centre(heading).WithX(Centre(heading).X + 12));

        Assert.Equal(["g1", "g2"], document.Board.Groups.Select(g => g.Id));
        Assert.False(document.CanUndo);
    }

    /// <summary>A press and a release on <paramref name="target"/> itself, at the same spot — what the
    /// heading's rename waits for, delivered where a headless pointer cannot reach.</summary>
    private static void ClickWithoutTravelling(Avalonia.Controls.Control target)
    {
        var pointer = new Pointer(0, PointerType.Mouse, isPrimary: true);
        target.RaiseEvent(new PointerPressedEventArgs(
            target, pointer, target, default, 0,
            new PointerPointProperties(default, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
        target.RaiseEvent(new PointerReleasedEventArgs(
            target, pointer, target, default, 0,
            new PointerPointProperties(default, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock Heading(Visual surface, string name) =>
        surface.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == name);

    /// <summary>Press on <paramref name="from"/>, travel to <paramref name="to"/>, and let go there.</summary>
    private static void Drag(Visual from, Point to)
    {
        var window = (Window)TopLevel.GetTopLevel(from)!;
        window.MouseDown(Centre(from), MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static Point Centre(Visual target)
    {
        var window = (Window)TopLevel.GetTopLevel(target)!;
        return target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
    }

    /// <summary>
    /// Pressing on the board outside the field you were typing in takes the caret out of it and commits
    /// what you typed. Avalonia only moves focus when the press lands on something that takes focus, and
    /// most of a board doesn't — so the caret used to sit blinking in a field the reader had visibly
    /// finished with, its edit uncommitted.
    /// </summary>
    [AvaloniaFact]
    public void Pressing_outside_a_field_takes_the_caret_out_of_it()
    {
        var (view, document) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;

        AddRow(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var field = (TextBox)AddRow(view);
        field.Text = "Buy milk";
        Assert.True(field.IsFocused);

        // The board's own background, well below the columns — nothing there takes focus.
        window.MouseDown(new Point(450, 560), MouseButton.Left);
        window.MouseUp(new Point(450, 560), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(document.Board.Groups[0].Tasks, t => t.Title == "Buy milk");
        Assert.IsType<Button>(AddRow(view)); // …and the field closed behind it
    }

    /// <summary>
    /// …and the click still does whatever it was going to do. Settling the field happens on the way back
    /// up, so the target acts first: doing it on the way down would let the commit's rebuild tear down the
    /// control the press was headed for, which trades one bug for another.
    /// </summary>
    [AvaloniaFact]
    public void The_click_that_closes_a_field_still_does_its_own_job()
    {
        var (view, document) = Open();

        AddRow(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        ((TextBox)AddRow(view)).Text = "Buy milk";

        // A card is focusable, so this is the case Avalonia already handled — but it must still open.
        Click(view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t3"));

        Assert.Contains(document.Board.Groups[0].Tasks, t => t.Title == "Buy milk");
        Assert.True(view.IsModalOpen);
    }

    /// <summary>
    /// The paper is recessed and the columns are raised, not the other way round.
    ///
    /// <para>The board painted no background at all, so it wore whatever the platform theme gave the
    /// window — a flat white sheet in light, a near-black one in dark — while the columns took the
    /// <em>inset</em> token meant for a well. That is the prototype's design inverted: its body is
    /// <c>--paper</c> and its columns and cards are <c>--surface</c>. Asserted against the tokens rather
    /// than against colours, so it stays true in both variants.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_board_is_paper_and_its_columns_are_cards_lying_on_it()
    {
        var (view, _) = Open();
        var palette = Puffinote.Components.Theming.Tokens.Active;

        Assert.Same(palette.SurfaceInset, view.BoardForTest.Background);

        var column = view.BoardForTest.GetLogicalDescendants().OfType<Border>().First(b => b.Width == 280);
        Assert.Same(palette.Surface, column.Background);
        Assert.Same(palette.Surface, view.BoardForTest.CardsForTest[0].Background);

        // Both readings lie on the same sheet, so switching views is not also a change of colour.
        view.Layout = BoardLayout.List;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(palette.SurfaceInset, view.ListForTest.Background);
    }

    /// <summary>
    /// The board's trailing "add a list" is a column stub, not a shrink-wrapped label. It used to be a
    /// filled button the width of its own two words, which read as a control that had wandered in at the
    /// end of the row rather than as the next slot along.
    /// </summary>
    [AvaloniaFact]
    public void The_add_a_list_ghost_is_as_wide_as_a_column_stub()
    {
        var (view, _) = Open();
        var add = AddListButton(view);

        Assert.Equal(200, add.Bounds.Width);

        // A ghost, not a filled button: the dashes are how a placeholder says it marks empty space.
        Assert.Contains(
            add.GetLogicalDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>(),
            r => r.StrokeDashArray is { Count: > 0 });
    }

    private static Button AddListButton(TaskBoardView view) =>
        view.BoardForTest.GetLogicalDescendants().OfType<Button>()
            .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Add a list");

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
    /// A card shows its star only when it is starred — or when the pointer is on it.
    ///
    /// <para>Starring is rare and unstarring rarer, so an outline star drawn on every card in every column
    /// was a hollow shape repeated down the whole board: noise the eye has to discard before it can find
    /// the cards that actually are starred. It stays reachable, because taking the affordance away would
    /// leave the board with no way to star anything at all.</para>
    /// </summary>
    [AvaloniaFact]
    public void An_unstarred_card_keeps_its_star_hidden_until_the_pointer_is_on_it()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;

        static Button Star(TaskCard card) =>
            card.GetLogicalDescendants().OfType<Button>().First(b =>
                Avalonia.Automation.AutomationProperties.GetName(b) is "Add star" or "Remove star");

        var starred = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t1");   // Important
        var plain = view.BoardForTest.CardsForTest.First(c => c.Task.Id == "t2");     // not

        Assert.Equal(1, Star(starred).Opacity);
        Assert.Equal(0, Star(plain).Opacity);

        // The pointer reaches the card before it can reach the star, so nothing is ever clicked blind.
        window.MouseMove(Centre(plain));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Star(plain).Opacity);

        window.MouseMove(Centre(starred));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, Star(plain).Opacity);
        Assert.Equal(1, Star(starred).Opacity); // …and a starred one never dims
    }

    /// <summary>
    /// The tick box is drawn whole.
    ///
    /// <para>Putting the checkbox on the title's line by setting its Height to the line height did not
    /// move it — it clipped it. The Fluent template centres a 20px box inside a 32px row and clips to its
    /// own bounds, so the box kept its offset and lost its bottom edge. Wrapping it in a fixed-height
    /// panel does the same thing, because an explicit Height is passed down as a measure constraint; the
    /// child has to be measured against infinity and then placed.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_tick_box_is_given_the_room_it_asks_for()
    {
        var (view, _) = Open();

        // What an untouched CheckBox wants, from this theme rather than from a number written down here.
        var reference = new CheckBox();
        var probe = new Window { Width = 200, Height = 100, Content = reference };
        probe.Show();
        probe.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        foreach (var card in view.BoardForTest.CardsForTest)
        {
            var check = card.GetLogicalDescendants().OfType<CheckBox>().First();
            Assert.True(
                check.Bounds.Height >= reference.Bounds.Height,
                $"the tick box on {card.Task.Id} is {check.Bounds.Height} tall but wants {reference.Bounds.Height}");

            var star = card.GetLogicalDescendants().OfType<Button>().First(b =>
                Avalonia.Automation.AutomationProperties.GetName(b) is "Add star" or "Remove star");
            Assert.True(
                star.Bounds.Height >= StarGlyphSize,
                $"the star on {card.Task.Id} is {star.Bounds.Height} tall around an {StarGlyphSize} glyph");
        }
    }

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

        var view = new TaskBoardView { Document = new BoardDocument(board), Today = Today, Layout = BoardLayout.Board };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.BoardForTest.GetLogicalDescendants().OfType<ScrollViewer>()
            .First(s => s.HorizontalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        scroll.Offset = new Vector(scroll.Extent.Width, 0); // clamped to the far end
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var add = AddListButton(view);
        double right = add.TranslatePoint(new Point(0, 0), scroll)!.Value.X + add.Bounds.Width;

        Assert.True(
            right <= scroll.Viewport.Width,
            $"its right edge is at {right} in a {scroll.Viewport.Width}-wide viewport");
    }

    /// <summary>
    /// Two controls at the two ends of the strip: how you are reading the board on the left, what you are
    /// reading of it on the right, and nothing in between.
    ///
    /// <para>The strip used to carry the search box, four filter pills and the switch, adaptively
    /// re-arranged when they stopped fitting. Four pills is a control that is three-quarters "not this
    /// one" — it spent the width of half a narrow window showing the three answers you didn't pick,
    /// permanently, to a question you ask occasionally.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_strip_is_the_view_switch_on_the_left_and_Filter_on_the_right()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;
        Resize(window, 1100);

        var switcher = SwitchButton(view);
        var filter = FilterButton(view);

        // One row: they share a centre line, not merely a top edge.
        Assert.Equal(Centre(switcher, view), Centre(filter, view), precision: 0);

        // The switch is hard left and Filter is hard right, with the board's width between them.
        Assert.True(Left(switcher, view) < 30, $"the switch starts at {Left(switcher, view)}");
        double gap = 1100 - Left(filter, view) - filter.Bounds.Width;
        Assert.True(gap < 30, $"Filter ends {gap} short of the right edge");
        Assert.True(Left(filter, view) > Left(switcher, view) + switcher.Bounds.Width + 400);

        // Nothing else is on the strip — the pills and the search box have gone into the panel.
        Assert.DoesNotContain(
            view.GetLogicalDescendants().OfType<TextBox>(), b => b.PlaceholderText == "Search tasks");
        Assert.DoesNotContain(
            view.GetLogicalDescendants().OfType<Button>(), b => (b.Content as string) == "All");
    }

    /// <summary>
    /// The Filter panel holds the search box and the slices together, with a tick against the one in
    /// force — they are one job ("which of these tasks am I looking at"), so they are one panel.
    /// </summary>
    [AvaloniaFact]
    public void The_filter_panel_holds_the_search_box_and_every_slice()
    {
        var (view, _) = Open();

        // Unfinished leads: "what is still on my plate" is the question a board is opened to answer, and
        // All is the one you drop back to when you want the record as well as the work.
        Assert.Equal(
            ["Unfinished", "All", "Starred", "Due tomorrow", "Due this week"],
            FilterRows(view).Select(Named));
        Assert.Same(Search(view).Parent, FilterPanel(view)); // the search box is in the panel, not the strip

        // A tick, not a fill: in a vertical list that is what "this is the one" looks like, and it survives
        // a reader who cannot tell the fill from the ground.
        Assert.Equal([null, "selected", null, null, null], FilterRows(view).Select(Help));

        Row(view, "Starred").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(TaskFilter.Starred, view.Filter);
        Assert.Equal([null, null, "selected", null, null], FilterRows(view).Select(Help));
    }

    /// <summary>
    /// The button says when the board is showing a slice of itself. A filtered board looks exactly like a
    /// board with fewer tasks on it, and the pills at least showed which one was lit — so the button that
    /// replaced them names the slice and wears the accent whenever a slice or a search is hiding anything.
    /// </summary>
    [AvaloniaFact]
    public void The_filter_button_says_what_is_being_hidden()
    {
        var (view, _) = Open();
        var button = FilterButton(view);

        static string? Label(Button b) =>
            b.GetLogicalDescendants().OfType<TextBlock>().First().Text;

        Assert.Equal("Filter", Label(button));
        Assert.Null(Avalonia.Automation.AutomationProperties.GetHelpText(button));

        view.Filter = TaskFilter.Unfinished;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Unfinished", Label(button));
        Assert.Same(Puffinote.Components.Theming.Tokens.Active.Accent, button.BorderBrush);

        // A search narrows the board without changing the slice, and has to say so too.
        view.Filter = TaskFilter.All;
        view.Query = "invoice";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Filter", Label(button));
        Assert.Contains("invoice", Avalonia.Automation.AutomationProperties.GetHelpText(button)!, StringComparison.Ordinal);
        Assert.Same(Puffinote.Components.Theming.Tokens.Active.Accent, button.BorderBrush);
    }

    /// <summary>
    /// <c>/</c> opens the filter panel — the search box lives in it now, so jumping to the search box means
    /// opening the panel first. ⌘F is the same journey from the command catalog.
    /// </summary>
    [AvaloniaFact]
    public void Slash_and_the_search_command_both_open_the_filter_panel()
    {
        var (view, _) = Open();
        var window = (Window)TopLevel.GetTopLevel(view)!;

        Assert.False(view.FilterMenu.IsOpen);

        window.KeyPressQwerty(PhysicalKey.Slash, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FilterMenu.IsOpen);

        view.FilterMenu.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.False(view.FilterMenu.IsOpen);

        view.EditingCommands.Single(c => c.Id == "edit.find").Execute();
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FilterMenu.IsOpen);

        view.FilterMenu.Hide(); // never leave a popup standing in a headless run
        Dispatcher.UIThread.RunJobs();
    }

    private static void Resize(Window window, double width)
    {
        window.Width = width;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static double Top(Visual part, Visual root) => part.TranslatePoint(new Point(0, 0), root)!.Value.Y;

    private static double Left(Visual part, Visual root) => part.TranslatePoint(new Point(0, 0), root)!.Value.X;

    private static double Centre(Visual part, Visual root) => Top(part, root) + (part.Bounds.Height / 2);

    /// <summary>The filter panel's own content, which exists whether or not the popup is showing.</summary>
    private static StackPanel FilterPanel(TaskBoardView view) => (StackPanel)view.FilterMenu.Content!;

    private static TextBox Search(TaskBoardView view) =>
        FilterPanel(view).GetLogicalDescendants().OfType<TextBox>().First(b => b.PlaceholderText == "Search tasks");

    private static List<Button> FilterRows(TaskBoardView view) =>
        [.. FilterPanel(view).GetLogicalDescendants().OfType<Button>()];

    private static Button Row(TaskBoardView view, string name) =>
        FilterRows(view).First(b => Named(b) == name);

    private static string? Named(Button b) => Avalonia.Automation.AutomationProperties.GetName(b);

    private static string? Help(Button b) => Avalonia.Automation.AutomationProperties.GetHelpText(b);

    private static Button FilterButton(TaskBoardView view) =>
        view.GetLogicalDescendants().OfType<Button>().First(b => Named(b) == "Filter");

    private static Button SwitchButton(TaskBoardView view) =>
        view.GetLogicalDescendants().OfType<Button>().First(b => Named(b) == "Board view");

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

        var view = new TaskBoardView { Document = new BoardDocument(board), Today = Today, Layout = BoardLayout.Board };
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
