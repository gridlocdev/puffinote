using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pivotask.Control.Views;
using Pivotask.Core;
using Pivotask.Core.Model;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// The task dialog: everything about one task, disclosed progressively. A task is usually a sentence,
/// and a dialog that opens with five empty sections implies you have failed to fill them in.
/// </summary>
public class TaskDialogTests
{
    private static readonly DateOnly Today = new(2026, 8, 7);

    private static (TaskBoardView View, BoardDocument Document, TaskItem Task) Open(Action<Board>? arrange = null)
    {
        var board = new Board();
        board.Labels.Add(new Pivotask.Core.Model.Label { Id = "ops", Name = "ops", Color = "#4d7c0f" });
        var inbox = new TaskGroup { Id = "g1", Name = "Inbox" };
        var week = new TaskGroup { Id = "g2", Name = "This week" };
        var task = new TaskItem { Id = "t1", Title = "Draft the retro agenda" };
        inbox.Tasks.Add(task);
        board.Groups.Add(inbox);
        board.Groups.Add(week);
        arrange?.Invoke(board);

        var document = new BoardDocument(board);
        var view = new TaskBoardView { Document = document, Today = Today, Layout = BoardLayout.Board };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();

        _ = view.OpenTaskAsync(task);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (view, document, task);
    }

    private static TaskDialog Dialog(TaskBoardView view) =>
        view.OverlayForTest.GetLogicalDescendants().OfType<TaskDialog>().Single();

    /// <summary>
    /// Today is unmistakable in the due-date calendar.
    ///
    /// <para>The stock theme marks it with a hairline a half-step off its neighbours, which on a grid of
    /// thirty near-identical numerals is not a difference you can find without hunting for it — and
    /// finding today is how "is this week's Thursday the 12th or the 19th?" gets answered. Three signals,
    /// not one, because a colour alone is not a difference every reader can see: a ring, a wash, and bold.</para>
    ///
    /// <para>Driven through a bare <see cref="Calendar"/> rather than the picker's popup: the styles are
    /// what is under test, the day grid only exists once a month is on screen, and a popup left standing
    /// in a headless run is one of the two ways this suite hangs.</para>
    /// </summary>
    [AvaloniaFact]
    public void Today_is_unmistakable_in_the_due_date_calendar()
    {
        var palette = Puffinote.Components.Theming.Tokens.Active;

        var calendar = new Calendar();
        CalendarChrome.MarkToday(calendar);

        var window = new Window { Width = 420, Height = 420, Content = calendar };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var days = calendar.GetVisualDescendants().OfType<CalendarDayButton>().ToList();
        Assert.NotEmpty(days);

        var today = Assert.Single(days, d => d.Classes.Contains(":today"));
        Assert.Same(palette.Accent, today.BorderBrush);
        Assert.Same(palette.AccentSubtle, today.Background);
        Assert.Equal(FontWeight.Bold, today.FontWeight);

        // …and it is the only square wearing it, or it would not be a mark.
        Assert.All(
            days.Where(d => !ReferenceEquals(d, today)),
            d => Assert.NotSame(palette.Accent, d.BorderBrush));
    }

    /// <summary>
    /// ⌘Enter is "Done", from whichever field the caret is in — and it keeps what was typed there.
    ///
    /// <para>Plain Enter cannot be the way out: it names the task in the title, adds an item in the steps
    /// field, and settles a step where it is, so in three of this dialog's fields the key that means
    /// "that's what it says" has to go on meaning that. The chord has to be handled on the way down for
    /// the same reason — each of those fields has an Enter handler that never looks at the modifiers, and
    /// whichever one held the caret would otherwise eat it.</para>
    /// </summary>
    [AvaloniaFact]
    public void Cmd_Enter_finishes_the_dialog_from_whichever_field_has_the_caret()
    {
        var (view, _, task) = Open(board => board.Groups[0].Tasks[0].Steps.Add(
            new TaskStep { Id = "s1", Text = "outline" }));

        var dialog = Dialog(view);
        var title = dialog.GetLogicalDescendants().OfType<TextBox>().First(b => b.Text == task.Title);

        title.Focus();
        Dispatcher.UIThread.RunJobs();
        title.Text = "Draft the retro";

        title.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            KeyModifiers = Puffinote.Components.Commands.Shortcut.Cmd(Key.Enter)
                .ModifiersFor(Puffinote.Components.Commands.Shortcut.IsMac),
        });
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.IsModalOpen);              // the dialog is gone…
        Assert.Equal("Draft the retro", task.Title); // …and the half-typed title went with it, not away
    }

    /// <summary>Nothing but the title and the notes until the task has more, or you ask for more.</summary>
    [AvaloniaFact]
    public void A_plain_task_opens_with_no_empty_sections()
    {
        var (view, _, _) = Open();
        var headings = Dialog(view).GetLogicalDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();

        Assert.DoesNotContain("LABELS", headings);
        Assert.DoesNotContain("DUE DATE", headings);
        Assert.DoesNotContain("STEPS", headings);
        Assert.Contains("NOTES", headings); // …but the note is on by default
    }

    /// <summary>
    /// One "+" opens the whole field list, rather than a row of "+ Labels" / "+ Due date" / "+ Steps"
    /// buttons sitting permanently under the notes. Three buttons is three things to read every time you
    /// open a task, to add something you usually don't.
    /// </summary>
    [AvaloniaFact]
    public void The_plus_menu_offers_every_field_and_reveals_the_one_picked()
    {
        var (view, _, _) = Open();
        var dialog = Dialog(view);

        Assert.Empty(Named(dialog, "+ Steps")); // the old row of buttons is gone
        Assert.Single(Named(dialog, "Add a field to this task"));

        var rows = MenuRows(dialog);
        Assert.Equal(["Labels", "Due date", "Steps"], rows.Select(Name));

        Click(rows.Single(r => Name(r) == "Steps"));

        Assert.Contains(
            "STEPS",
            dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
    }

    /// <summary>
    /// A field the task already has stays in the menu and is marked "on", so the menu is a complete map
    /// of what a task can hold rather than a list that shrinks as you use it.
    /// </summary>
    [AvaloniaFact]
    public void A_field_the_task_already_has_is_listed_and_marked_on()
    {
        var (view, _, _) = Open(board => board.Groups[0].Tasks[0].Due = new DateOnly(2026, 8, 20));
        var rows = MenuRows(Dialog(view));

        Assert.Equal("on", Avalonia.Automation.AutomationProperties.GetHelpText(rows.Single(r => Name(r) == "Due date")));
        Assert.Null(Avalonia.Automation.AutomationProperties.GetHelpText(rows.Single(r => Name(r) == "Labels")));
    }

    /// <summary>
    /// The list picker moves the task, which before this was a thing the GUI could only do by dragging —
    /// so a board whose drag felt wrong had no way to move a task at all except the CLI.
    /// </summary>
    [AvaloniaFact]
    public void The_header_moves_the_task_to_another_list()
    {
        var (view, document, task) = Open();
        var picker = Dialog(view).GetLogicalDescendants().OfType<ComboBox>()
            .Single(c => Name(c) == "Move to list");

        Assert.Equal(["INBOX", "THIS WEEK"], picker.ItemsSource!.Cast<string>());
        Assert.Equal(0, picker.SelectedIndex);

        picker.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("g2", document.Board.Locate(task.Id)!.Value.Group.Id);
        Assert.Equal("Move Task", document.UndoLabel);
    }

    /// <summary>Star and tick from the dialog, both through the undo stack like everything else.</summary>
    [AvaloniaFact]
    public void The_header_stars_the_task_and_the_title_row_ticks_it()
    {
        var (view, document, task) = Open();
        var dialog = Dialog(view);

        Named(dialog, "Add star").Single().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.Important);
        Assert.Single(Named(dialog, "Remove star")); // …and the button now offers the other direction

        dialog.GetLogicalDescendants().OfType<CheckBox>()
            .First(c => Name(c) == "Mark finished").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(task.Done);
        document.Undo();
        Assert.False(task.Done);
    }

    /// <summary>
    /// The footer is the only way to throw a task away — there was none at all before. It doesn't ask
    /// first: one ⌘Z puts the task back exactly as it was.
    /// </summary>
    [AvaloniaFact]
    public void The_footer_deletes_the_task_and_undo_puts_it_back()
    {
        var (view, document, task) = Open();

        Click(Named(Dialog(view), "Delete task").Single());
        Dispatcher.UIThread.RunJobs();

        Assert.Null(document.Board.Locate(task.Id));
        Assert.False(view.IsModalOpen);

        document.Undo();
        Assert.Equal("g1", document.Board.Locate(task.Id)!.Value.Group.Id);
    }

    /// <summary>
    /// A step can be ticked, reworded, and thrown away. Only the first of those existed: a checklist is
    /// written in a hurry, so fixing and pruning it is most of what you do to one.
    /// </summary>
    [AvaloniaFact]
    public void A_step_can_be_reworded_and_deleted()
    {
        var (view, document, task) = Open(board =>
        {
            board.Groups[0].Tasks[0].Steps.Add(new TaskStep { Id = "s1", Text = "Book the room" });
            board.Groups[0].Tasks[0].Steps.Add(new TaskStep { Id = "s2", Text = "Send the invite" });
        });
        var dialog = Dialog(view);

        var text = dialog.GetLogicalDescendants().OfType<TextBox>().First(b => b.Text == "Book the room");
        text.Text = "Book the big room";
        text.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Book the big room", task.Steps[0].Text);
        Assert.Equal("Edit Step", document.UndoLabel);

        Click(Named(Dialog(view), "Delete step: Send the invite").Single());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Book the big room"], task.Steps.Select(s => s.Text));
    }

    /// <summary>An empty step is a slip rather than a rename, so the old text comes back.</summary>
    [AvaloniaFact]
    public void Emptying_a_step_puts_its_words_back()
    {
        var (view, document, task) = Open(board =>
            board.Groups[0].Tasks[0].Steps.Add(new TaskStep { Id = "s1", Text = "Book the room" }));

        var text = Dialog(view).GetLogicalDescendants().OfType<TextBox>().First(b => b.Text == "Book the room");
        text.Text = "   ";
        text.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Book the room", task.Steps[0].Text);
        Assert.False(document.CanUndo);
    }

    /// <summary>How far through the checklist you are, as a reading and as a bar.</summary>
    [AvaloniaFact]
    public void The_checklist_says_how_far_through_it_is()
    {
        var (view, _, _) = Open(board =>
        {
            var steps = board.Groups[0].Tasks[0].Steps;
            steps.Add(new TaskStep { Id = "s1", Text = "one", Done = true });
            steps.Add(new TaskStep { Id = "s2", Text = "two" });
            steps.Add(new TaskStep { Id = "s3", Text = "three" });
        });

        var text = Dialog(view).GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("1 of 3", text);
        Assert.Contains("33%", text);
    }

    /// <summary>"Delete all" is one command, so one undo puts the whole checklist back.</summary>
    [AvaloniaFact]
    public void Delete_all_clears_the_checklist_in_one_undoable_step()
    {
        var (view, document, task) = Open(board =>
        {
            var steps = board.Groups[0].Tasks[0].Steps;
            steps.Add(new TaskStep { Id = "s1", Text = "one" });
            steps.Add(new TaskStep { Id = "s2", Text = "two" });
        });

        Click(Named(Dialog(view), "Delete all").Single());
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(task.Steps);
        document.Undo();
        Assert.Equal(2, task.Steps.Count);
    }

    /// <summary>
    /// The due section says what its date means in the card's own words, and offers the one thing a date
    /// field otherwise makes awkward: taking the date off again.
    /// </summary>
    [AvaloniaFact]
    public void The_due_section_reads_the_date_back_and_can_clear_it()
    {
        var (view, _, task) = Open(board => board.Groups[0].Tasks[0].Due = new DateOnly(2026, 8, 5));
        var dialog = Dialog(view);

        Assert.Contains(
            "2 days late",
            dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

        Click(Named(dialog, "Clear").Single());
        Dispatcher.UIThread.RunJobs();

        Assert.Null(task.Due);
        Assert.DoesNotContain(
            "DUE DATE",
            dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
    }

    /// <summary>
    /// The dialog's star is the card's star — one control, so there is one answer to what starred looks
    /// like rather than a drawn glyph in one place and a "★" character in the other.
    /// </summary>
    [AvaloniaFact]
    public void The_dialog_and_the_card_draw_the_same_star()
    {
        var (view, _, task) = Open(board => board.Groups[0].Tasks[0].Important = true);

        var inDialog = Named(Dialog(view), "Remove star").Single()
            .GetLogicalDescendants().OfType<Puffinote.Components.Controls.Icons.LucideIcon>().Single();
        var onCard = view.BoardForTest.CardsForTest.Single(c => c.Task.Id == task.Id)
            .GetLogicalDescendants().OfType<Puffinote.Components.Controls.Icons.LucideIcon>()
            .Single(i => i.IconKey == "star");

        Assert.Equal(onCard.IconKey, inDialog.IconKey);
        Assert.Equal(onCard.Size, inDialog.Size);
        Assert.Equal(onCard.IsFilled, inDialog.IsFilled);
    }

    /// <summary>
    /// Nothing in the dialog is an emoji. An emoji is a colour bitmap the platform picks: it ignores the
    /// button's foreground, so it can't follow the theme, and it matches nothing else on screen.
    /// </summary>
    [AvaloniaFact]
    public void Delete_task_wears_a_drawn_icon_rather_than_an_emoji()
    {
        var (view, _, _) = Open();
        var dialog = Dialog(view);

        Assert.Contains(
            Named(dialog, "Delete task").Single()
                .GetLogicalDescendants().OfType<Puffinote.Components.Controls.Icons.LucideIcon>(),
            i => i.IconKey == "trash-2");

        foreach (var text in dialog.GetLogicalDescendants().OfType<TextBlock>())
        {
            Assert.DoesNotContain("🗑", text.Text ?? string.Empty, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The dialog's fields keep clear of the card's edge, as the prototype's 24px body inset does. The
    /// note ran flush to the right edge with the scrollbar drawn over it, which is what made the whole
    /// dialog read as cramped next to the mockup.
    /// </summary>
    [AvaloniaFact]
    public void The_fields_keep_clear_of_the_cards_edge()
    {
        var (view, _, _) = Open(board =>
        {
            board.Groups[0].Tasks[0].Due = new DateOnly(2026, 8, 12);
            board.Groups[0].Tasks[0].Steps.Add(new TaskStep { Id = "s1", Text = "hello" });
        });

        var dialog = Dialog(view);

        // The floating card the overlay wraps a dialog in — the edge a reader actually sees.
        var card = view.OverlayForTest.GetLogicalDescendants().OfType<Border>()
            .First(b => b.GetLogicalDescendants().OfType<TaskDialog>().Any());

        const double gutter = 24;
        foreach (var box in dialog.GetLogicalDescendants().OfType<TextBox>())
        {
            double left = box.TranslatePoint(new Point(0, 0), card)!.Value.X;
            double right = card.Bounds.Width - left - box.Bounds.Width;

            Assert.True(left >= gutter, $"“{box.PlaceholderText ?? box.Text}” sits {left} from the left edge");
            Assert.True(right >= gutter, $"“{box.PlaceholderText ?? box.Text}” sits {right} from the right edge");
        }
    }

    /// <summary>
    /// A step's words sit on the same line as the tick that labels them. The box was already centred in
    /// the row; its text was not — a TextBox stretches its presenter by default, so in a box as tall as
    /// the checkbox beside it a single line rides along the top.
    /// </summary>
    [AvaloniaFact]
    public void A_steps_words_are_centred_against_its_tick()
    {
        var (view, _, _) = Open(board =>
            board.Groups[0].Tasks[0].Steps.Add(new TaskStep { Id = "s1", Text = "Book the room" }));

        var text = Dialog(view).GetLogicalDescendants().OfType<TextBox>().First(b => b.Text == "Book the room");
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, text.VerticalContentAlignment);

        // The line the reader sees, not just the box around it.
        var presenter = text.GetVisualDescendants()
            .First(v => v.GetType().Name.Contains("TextPresenter", StringComparison.Ordinal));
        var check = ((Avalonia.Controls.Control)text.Parent!).GetLogicalDescendants().OfType<CheckBox>().First();

        static double Centre(Visual part, Visual root) =>
            part.TranslatePoint(new Point(0, 0), root)!.Value.Y + (part.Bounds.Height / 2);

        Assert.Equal(Centre(check, text), Centre(presenter, text), precision: 0);
    }

    /// <summary>
    /// Enter on a step means "that's what it says", not "and now keep typing into it" — it commits and
    /// hands the keyboard back to whatever had it before, because editing a checklist item is nearly
    /// always an interruption of something else.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_step_commits_it_and_gives_the_keyboard_back()
    {
        var (view, document, task) = Open(board =>
            board.Groups[0].Tasks[0].Steps.Add(new TaskStep { Id = "s1", Text = "Book the room" }));
        var dialog = Dialog(view);

        // Typing in "add an item", then breaking off to fix the line above.
        var adder = dialog.GetLogicalDescendants().OfType<TextBox>().First(b => Name(b) == "Add a step");
        adder.Focus();
        Dispatcher.UIThread.RunJobs();

        var text = dialog.GetLogicalDescendants().OfType<TextBox>().First(b => b.Text == "Book the room");
        text.Focus();
        Dispatcher.UIThread.RunJobs();
        text.Text = "Book the big room";

        text.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Book the big room", task.Steps[0].Text);
        Assert.Equal("Edit Step", document.UndoLabel);
        Assert.True(adder.IsFocused, "the caret did not go back to the field it came from");
    }

    /// <summary>With nowhere to go back to, Enter still takes the caret out of the line — a committed
    /// field that keeps it looks like one that is still being edited.</summary>
    [AvaloniaFact]
    public void Enter_on_a_step_leaves_the_field_even_with_nowhere_to_return_to()
    {
        var (view, _, task) = Open(board =>
            board.Groups[0].Tasks[0].Steps.Add(new TaskStep { Id = "s1", Text = "Book the room" }));

        var text = Dialog(view).GetLogicalDescendants().OfType<TextBox>().First(b => b.Text == "Book the room");
        text.Focus();
        Dispatcher.UIThread.RunJobs();

        text.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.False(text.IsFocused);
        Assert.Equal("Book the room", task.Steps[0].Text); // unchanged, and still there
    }

    /// <summary>
    /// Enter names the task; it does not answer the dialog. The keystroke used to reach the footer's
    /// default button and shut the whole thing — you typed a title, pressed the key that means "done with
    /// this line", and the task you were still working on went away.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_the_title_names_the_task_without_closing_the_dialog()
    {
        var (view, document, task) = Open();
        var dialog = Dialog(view);

        var title = dialog.GetLogicalDescendants().OfType<TextBox>()
            .First(b => b.Text == "Draft the retro agenda");
        title.Focus();
        Dispatcher.UIThread.RunJobs();
        title.Text = "Draft the Q3 retro agenda";

        title.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Draft the Q3 retro agenda", task.Title);
        Assert.Equal("Rename Task", document.UndoLabel);
        Assert.True(view.IsModalOpen, "Enter closed the dialog");
        Assert.False(title.IsFocused); // …and the caret came out of the line, as on a step
    }

    /// <summary>
    /// A step keeps its resting background under the pointer but fills while it holds the caret. The
    /// stock TextBox washes on hover, which down a checklist meant every row lit up as the pointer
    /// crossed it — the rows are a list to read, not a menu to pick from. The field being <em>edited</em>
    /// is the one that should stand out, and hover must not cancel that: on a real desk the field you are
    /// typing into is nearly always the one under the pointer too.
    /// </summary>
    [AvaloniaFact]
    public void A_step_fills_while_being_edited_but_not_merely_under_the_pointer()
    {
        var (view, _, _) = Open(board =>
            board.Groups[0].Tasks[0].Steps.Add(new TaskStep { Id = "s1", Text = "Book the room" }));

        var text = Dialog(view).GetLogicalDescendants().OfType<TextBox>().First(b => b.Text == "Book the room");

        // The fill lives on the template's own border, not on the box — that is why setting Background
        // on the TextBox could not turn it off.
        var fill = text.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
        var window = (Window)TopLevel.GetTopLevel(view)!;
        var over = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), window)!.Value;
        var away = new Point(4, 4);

        string StateAt(Point pointer, bool editing)
        {
            if (editing)
            {
                text.Focus();
            }

            window.MouseMove(pointer);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            return Describe(fill.Background);
        }

        string resting = StateAt(away, editing: false);
        Assert.Equal(resting, StateAt(over, editing: false));
        Assert.True(text.IsPointerOver, "the pointer never reached the step");

        string editingAway = StateAt(away, editing: true);
        Assert.NotEqual(resting, editingAway);

        // …and being hovered as well as edited does not undo it, which is the case a ":pointerover"
        // override on its own quietly broke.
        Assert.Equal(editingAway, StateAt(over, editing: true));
    }

    /// <summary>A brush as a comparable string — two Transparent brushes are not the same object.</summary>
    private static string Describe(IBrush? brush) => brush switch
    {
        null => "none",
        ISolidColorBrush solid => solid.Color.ToString(),
        _ => brush.GetType().Name,
    };

    /// <summary>Open the "+" menu and return its rows.</summary>
    private static IReadOnlyList<Button> MenuRows(TaskDialog dialog)
    {
        var button = Named(dialog, "Add a field to this task").Single();
        dialog.AddFieldMenu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();

        var panel = (StackPanel)dialog.AddFieldMenu.Content!;
        return [.. panel.Children.OfType<Button>()];
    }

    private static string? Name(Avalonia.Controls.Control control) =>
        Avalonia.Automation.AutomationProperties.GetName(control);

    private static IReadOnlyList<Button> Named(Avalonia.Controls.Control root, string name) =>
        [.. root.GetLogicalDescendants().OfType<Button>().Where(b => Name(b) == name || b.Content as string == name)];

    private static void Click(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
