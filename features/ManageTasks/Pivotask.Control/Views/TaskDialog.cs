using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// Everything about one task: its title, its notes, its checklist, its labels, and its due date.
///
/// <para><b>Progressive disclosure</b>, as the prototype has it: labels, the due date, and the steps
/// section only appear once the task has them or you ask for them from the "+" menu. A task is usually
/// a sentence, and a dialog that opens with five empty sections implies you have failed to fill them in.</para>
///
/// <para>Edits go straight through the document as they are made, so every one is undoable and there is
/// no OK/Cancel to mean something ambiguous about a checklist you already ticked.</para>
/// </summary>
internal sealed class TaskDialog : ModalDialog<bool>
{
    private readonly BoardDocument _document;
    private readonly TaskItem _task;
    private readonly StackPanel _body = new() { Spacing = 22 };
    private readonly StackPanel _upper = new() { Spacing = 22 };
    private readonly StackPanel _lower = new() { Spacing = 22 };
    private readonly DockPanel _head = new() { LastChildFill = true };
    private readonly DateOnly _today;
    private bool _focusStepAdd;

    public TaskDialog(BoardDocument document, TaskItem task, DateOnly today)
    {
        _document = document;
        _task = task;
        _today = today;

        // No "Task" heading over the header strip: the strip already says which list this is in, and the
        // dialog's own subject is the title in 21pt underneath it. A band repeating the word "Task" would
        // be the third thing on screen saying so.
        Title = string.Empty;

        var palette = Tokens.Active;

        var title = new TextBox { Text = task.Title, AcceptsReturn = false, FontSize = 18, FontWeight = FontWeight.SemiBold };
        title.LostFocus += (_, _) =>
        {
            if ((title.Text ?? string.Empty) != task.Title)
            {
                _document.Execute(BoardCommands.SetTitle(task, title.Text ?? string.Empty));
            }
        };

        // The title row is the task in miniature: tick it, name it, and reach the rest of what it could
        // be. The "+" sits beside the one thing every task has, because that is what it offers more of.
        var titleRow = new DockPanel { LastChildFill = true };

        _tick = new CheckBox
        {
            IsChecked = task.Done,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Avalonia.Automation.AutomationProperties.SetName(_tick, "Mark finished");
        _tick.IsCheckedChanged += (_, _) =>
        {
            if ((_tick.IsChecked == true) != _task.Done)
            {
                // Completing cascades to the steps, so the checklist under it has to be redrawn.
                _document.Execute(BoardCommands.SetDone(_task, _tick.IsChecked == true));
                RebuildOptional();
            }
        };
        DockPanel.SetDock(_tick, Dock.Left);
        titleRow.Children.Add(_tick);

        var add = BuildAddButton();
        DockPanel.SetDock(add, Dock.Right);
        titleRow.Children.Add(add);
        titleRow.Children.Add(title);
        _body.Children.Add(titleRow);

        var notes = new TextBox
        {
            Text = task.Notes,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 86,

            // Plain text, deliberately (proposal 0004 §10.4). A note attached to a task is a sentence or
            // two; if it genuinely wants to be a document, that document is a .md file.
            PlaceholderText = "Add a more detailed note…",
        };
        notes.LostFocus += (_, _) =>
        {
            if ((notes.Text ?? string.Empty) != task.Notes)
            {
                _document.Execute(BoardCommands.SetNotes(task, notes.Text ?? string.Empty));
            }
        };

        // Labels and the due date above the note, the checklist below it — the prototype's order, and the
        // order a task is written in: what it is, when it is wanted, what it is about, what it takes. The
        // note keeps a panel of its own between the two so it is never rebuilt out from under a caret.
        _body.Children.Add(_upper);
        _body.Children.Add(Section("align-left", "Notes", null, null, notes));
        _body.Children.Add(_lower);

        // Head and foot are pinned; only the task's own fields scroll. A checklist of twenty steps must
        // not push "Delete task" — or the way out of the dialog — off the bottom of the card.
        var root = new DockPanel { LastChildFill = true, Width = 512 };
        Avalonia.Automation.AutomationProperties.SetName(root, "Task details");

        var head = new Border
        {
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 12),
            Margin = new Thickness(0, 0, 0, 18),
            Child = _head,
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var foot = new Border
        {
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 14, 0, 0),
            Margin = new Thickness(0, 20, 0, 0),
            Child = BuildFoot(),
        };
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        // The gutter is the fields' own margin, not the scroller's padding — so it belongs to the scrollable
        // extent (the board learned this the hard way) and the scrollbar rides outside the text rather than
        // over it. Eight on top of the card's own sixteen is the prototype's 24px body inset.
        _body.Margin = new Thickness(8, 4, 8, 8);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            MaxHeight = 460,
            Content = _body,
        });

        DialogContent = root;
        RebuildHead();
        RebuildOptional();
    }

    private readonly CheckBox _tick;

    /// <summary>Which optional sections the user has asked for, on top of the ones the task already has.</summary>
    private readonly HashSet<string> _revealed = [];

    /// <summary>
    /// The header strip: which list the task is in, whether it is starred, and the way out.
    ///
    /// <para>The list picker is not decoration — it was the <em>only</em> thing missing that made moving a
    /// task a drag-and-drop-or-nothing affair in the GUI. Proposal 0004 §7 says the feature must never be
    /// blocked on drag feeling right, and `ptask move` cannot be the only alternative for someone who is
    /// looking at the task on screen.</para>
    /// </summary>
    private void RebuildHead()
    {
        var palette = Tokens.Active;
        _head.Children.Clear();

        var groups = _document.Board.Groups.ToList();
        var current = _document.Board.Locate(_task.Id)?.Group;

        var picker = new ComboBox
        {
            ItemsSource = groups.Select(g => g.Name.ToUpper(System.Globalization.CultureInfo.CurrentCulture)).ToList(),
            SelectedIndex = current is null ? -1 : groups.IndexOf(current),
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            MaxWidth = 230,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(picker, "Move to list");

        // Wired after the initial selection, so opening the dialog is not itself a move.
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0 || groups[picker.SelectedIndex] is not { } target)
            {
                return;
            }

            if (_document.Board.Locate(_task.Id)?.Group is { } from && !ReferenceEquals(from, target))
            {
                _document.Execute(BoardCommands.MoveTask(_document.Board, _task, target));
            }
        };
        DockPanel.SetDock(picker, Dock.Left);
        _head.Children.Add(picker);

        var close = IconButton("x", "Close details", palette);
        close.Click += (_, _) => Close(true);
        DockPanel.SetDock(close, Dock.Right);
        _head.Children.Add(close);

        // The card's star, not a second one that happens to look similar.
        var star = StarButton.Build(_task.Important, () =>
        {
            _document.Execute(BoardCommands.SetImportant(_task, !_task.Important));
            RebuildHead();
        });
        star.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(star, Dock.Right);
        _head.Children.Add(star);

        _head.Children.Add(new Panel()); // the gap between the picker and the two buttons
    }

    /// <summary>
    /// The footer: throw the task away, or stop looking at it.
    ///
    /// <para>Deleting does not ask first, unlike deleting a group. The prototype asks because a web page
    /// has no undo to offer; here every mutation is a command and one ⌘Z puts the task back, exactly as
    /// it was, in the group it was in. A group is different — it is a column of other people's work, and
    /// coarse enough to be worth a question.</para>
    /// </summary>
    private Avalonia.Controls.Control BuildFoot()
    {
        var row = new DockPanel { LastChildFill = true };

        // A drawn glyph, not "🗑". An emoji is a colour bitmap the platform picks: it ignores the button's
        // foreground, sits at whatever size and baseline the font decided, and on this dark chrome renders
        // as a pale grey bin that matches nothing else on screen. Every other icon in the family is a
        // stroked Lucide path that inherits its ink, and this is not the place to be the exception.
        var trash = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        trash.Children.Add(new LucideIcon { IconKey = "trash-2", Size = 14, VerticalAlignment = VerticalAlignment.Center });
        trash.Children.Add(new TextBlock { Text = "Delete task", VerticalAlignment = VerticalAlignment.Center });

        var delete = new Button { Content = trash };
        Avalonia.Automation.AutomationProperties.SetName(delete, "Delete task");
        delete.Click += (_, _) =>
        {
            if (_document.Board.Locate(_task.Id) is { } located)
            {
                _document.Execute(BoardCommands.DeleteTask(located.Group, _task));
            }

            Close(true);
        };
        DockPanel.SetDock(delete, Dock.Left);
        row.Children.Add(delete);

        var done = new Button { Content = "Done", IsDefault = true };
        Avalonia.Automation.AutomationProperties.SetName(done, "Done");
        done.Click += (_, _) => Close(true);
        DockPanel.SetDock(done, Dock.Right);
        row.Children.Add(done);

        row.Children.Add(new Panel());
        return row;
    }

    private static Button IconButton(string icon, string tip, ThemePalette palette, bool filled = false)
    {
        var button = new Button
        {
            Content = new LucideIcon { IconKey = icon, Size = 16, IsFilled = filled },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
            Foreground = palette.MutedText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    private void RebuildOptional()
    {
        _upper.Children.Clear();
        _lower.Children.Clear();

        if (IsShown("labels"))
        {
            _upper.Children.Add(Section("tag", "Labels", null, null, BuildLabels()));
        }

        if (IsShown("due"))
        {
            _upper.Children.Add(BuildDueSection());
        }

        if (IsShown("steps"))
        {
            _lower.Children.Add(BuildStepsSection());
        }
    }

    /// <summary>
    /// One section: a glyph out in the left margin, an upper-case heading, and the field itself.
    ///
    /// <para>The heading carries two optional passengers, because the prototype's do and they earn it:
    /// a <paramref name="note"/> that states the section's answer at a glance ("Tomorrow", "1 of 3"),
    /// and a <paramref name="link"/> that empties it. Putting "Clear" beside the heading rather than
    /// under the field keeps the destructive control away from the one you are typing into.</para>
    /// </summary>
    private static Avalonia.Controls.Control Section(
        string icon,
        string heading,
        Avalonia.Controls.Control? note,
        Button? link,
        Avalonia.Controls.Control body)
    {
        var palette = Tokens.Active;

        var headRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 9) };
        var title = new TextBlock
        {
            Text = heading.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.9,
            Foreground = palette.MutedText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(title, Dock.Left);
        headRow.Children.Add(title);

        if (note is not null)
        {
            note.Margin = new Thickness(10, 0, 0, 0);
            note.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(note, Dock.Left);
            headRow.Children.Add(note);
        }

        if (link is not null)
        {
            DockPanel.SetDock(link, Dock.Right);
            headRow.Children.Add(link);
        }

        headRow.Children.Add(new Panel());

        var stack = new StackPanel { Children = { headRow, body } };

        var row = new DockPanel { LastChildFill = true };
        var glyph = new LucideIcon
        {
            IconKey = icon,
            Size = 15,
            Foreground = palette.MutedText,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 12, 0),
        };
        DockPanel.SetDock(glyph, Dock.Left);
        row.Children.Add(glyph);
        row.Children.Add(stack);
        return row;
    }

    /// <summary>A quiet text button — the prototype's <c>.link</c>: "Clear", "Delete all".</summary>
    private static Button LinkButton(string text, Action run)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(7, 2),
            Foreground = Tokens.Active.MutedText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => run();
        return button;
    }

    /// <summary>
    /// The board's labels as chips you turn on and off, coloured as they are on the card. They were plain
    /// toggle buttons, which meant the one place you <em>choose</em> a label was the one place its colour
    /// wasn't — so picking "design" told you nothing about which chip would appear.
    /// </summary>
    private Avalonia.Controls.Control BuildLabels()
    {
        var palette = Tokens.Active;
        var panel = new WrapPanel();

        foreach (var label in _document.Board.Labels)
        {
            bool on = _task.Labels.Contains(label.Id, StringComparer.Ordinal);
            var color = Color.TryParse(label.Color, out var parsed) ? parsed : Color.FromRgb(0x64, 0x74, 0x8B);

            var chip = new Button
            {
                Content = label.Name,
                FontSize = 11,
                Padding = new Thickness(9, 4),
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Background = on ? new SolidColorBrush(color) : Brushes.Transparent,
                BorderBrush = on ? Brushes.Transparent : palette.SurfaceBorder,
                Foreground = on ? Brushes.White : palette.MutedText,
            };
            Avalonia.Automation.AutomationProperties.SetName(chip, label.Name);
            Avalonia.Automation.AutomationProperties.SetHelpText(chip, on ? "on" : null);

            chip.Click += (_, _) =>
            {
                _document.Execute(BoardCommands.ToggleLabel(_task, label.Id));

                // Keep the section open even when that was the last label: you are mid-decision, and a
                // section that vanished under the pointer would take the way back with it.
                _revealed.Add("labels");
                RebuildOptional();
            };
            panel.Children.Add(chip);
        }

        if (_document.Board.Labels.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "This board has no labels yet.", Foreground = palette.MutedText });
        }

        return panel;
    }

    /// <summary>
    /// The due date, with what it means spelled out beside the heading — "Tomorrow", "2 days late" — in
    /// the same words the card uses, so the pill you are about to create is legible before it exists.
    /// </summary>
    private Avalonia.Controls.Control BuildDueSection()
    {
        // A date, not a date and a time. CalendarDatePicker is the stock control for exactly this, and it
        // reads and writes the reader's own date order rather than making them type YYYY-MM-DD.
        var picker = new CalendarDatePicker
        {
            SelectedDate = _task.Due is { } due ? due.ToDateTime(TimeOnly.MinValue) : null,
            Width = 210,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Avalonia.Automation.AutomationProperties.SetName(picker, "Due date");

        picker.SelectedDateChanged += (_, _) =>
        {
            DateOnly? chosen = picker.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null;
            if (chosen == _task.Due)
            {
                return; // the rebuild that set the box, not a person changing their mind
            }

            _document.Execute(BoardCommands.SetDue(_task, chosen));
            _revealed.Add("due");
            RebuildOptional();
        };

        TextBlock? note = null;
        Button? clear = null;
        if (_task.Due is { } current)
        {
            bool late = !_task.Done && current < _today;
            note = new TextBlock
            {
                Text = BoardQuery.DescribeDue(current, _today),
                FontSize = 11,
                Foreground = late ? new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x66)) : Tokens.Active.MutedText,
            };

            clear = LinkButton("Clear", () =>
            {
                _document.Execute(BoardCommands.SetDue(_task, null));

                // Clearing a date is asking for the section to go away, not for an empty one to sit there.
                _revealed.Remove("due");
                RebuildOptional();
            });
        }

        return Section("calendar", "Due date", note, clear, picker);
    }

    /// <summary>
    /// The checklist: how far through it you are, then a row per step you can tick, reword, or throw
    /// away, then the field that adds the next one.
    ///
    /// <para>All three of those verbs were missing. A step could be ticked and nothing else — no way to
    /// fix a typo in one, no way to remove one, which for a list written in a hurry is most of what you
    /// do to it. "Delete all" is one command and therefore one undo, because clearing a checklist by
    /// mistake should cost one press to put right, not one per item.</para>
    /// </summary>
    private Avalonia.Controls.Control BuildStepsSection()
    {
        var palette = Tokens.Active;
        var panel = new StackPanel { Spacing = 2 };
        var (done, total) = _task.StepProgress;

        if (total > 0)
        {
            int percent = (int)Math.Round(done * 100.0 / total);
            var progress = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            var reading = new TextBlock
            {
                Text = $"{percent}%",
                FontSize = 11,
                Width = 32,
                Foreground = palette.MutedText,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(reading, Dock.Left);
            progress.Children.Add(reading);
            progress.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = total,
                Value = done,
                Height = 4,
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(progress);
        }

        foreach (var step in _task.Steps)
        {
            panel.Children.Add(BuildStepRow(step));
        }

        var add = new TextBox { PlaceholderText = "Add an item", Margin = new Thickness(0, 8, 0, 0) };
        Avalonia.Automation.AutomationProperties.SetName(add, "Add a step");
        add.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter || string.IsNullOrWhiteSpace(add.Text))
            {
                return;
            }

            // Same rule as the board's add row: a checklist is written in a run, so the field keeps the
            // keyboard rather than handing it back after each item.
            _focusStepAdd = true;
            _document.Execute(BoardCommands.AddStep(_task, add.Text.Trim()));
            RebuildOptional();
            e.Handled = true;
        };

        if (_focusStepAdd)
        {
            _focusStepAdd = false;
            add.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => add.Focus());
        }

        panel.Children.Add(add);

        var note = total > 0
            ? new TextBlock { Text = $"{done} of {total}", FontSize = 11, Foreground = palette.MutedText }
            : null;

        var clear = total > 0
            ? LinkButton("Delete all", () =>
            {
                // One command, so one undo puts the whole checklist back.
                _document.Execute(BoardCommands.ClearSteps(_task));
                _revealed.Remove("steps");
                RebuildOptional();
            })
            : null;

        return Section("square-check", "Steps", note, clear, panel);
    }

    private Avalonia.Controls.Control BuildStepRow(TaskStep step)
    {
        var palette = Tokens.Active;
        var row = new DockPanel { LastChildFill = true };

        var check = new CheckBox { IsChecked = step.Done, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(check, $"Toggle step: {step.Text}");
        check.IsCheckedChanged += (_, _) =>
        {
            if ((check.IsChecked == true) != step.Done)
            {
                _document.Execute(BoardCommands.SetStepDone(step, check.IsChecked == true));
                RebuildOptional(); // the progress reading moved
            }
        };
        DockPanel.SetDock(check, Dock.Left);
        row.Children.Add(check);

        var kill = IconButton("x", $"Delete step: {step.Text}", palette);
        kill.Click += (_, _) =>
        {
            _document.Execute(BoardCommands.DeleteStep(_task, step));

            // Deleting the last step is not a request to close the section — you are editing the list.
            _revealed.Add("steps");
            RebuildOptional();
        };
        DockPanel.SetDock(kill, Dock.Right);
        row.Children.Add(kill);

        // A text box that looks like text until you are in it. A checklist item is read far more often
        // than it is rewritten, so a row of framed fields would draw the eye to the wrong thing.
        var text = new TextBox
        {
            Text = step.Text,
            AcceptsReturn = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2),
            VerticalAlignment = VerticalAlignment.Center,

            // The box was already centred in the row; its *text* was not. A TextBox stretches its
            // presenter by default, which for a single-line field means the line sits at the top of a box
            // as tall as the checkbox beside it — six pixels above the tick it is supposed to label.
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = step.Done ? palette.MutedText : palette.Text,
        };
        Avalonia.Automation.AutomationProperties.SetName(text, "Step");

        void Commit()
        {
            string typed = (text.Text ?? string.Empty).Trim();

            // An empty step is a slip rather than a rename — the same rule a group heading follows.
            if (typed.Length == 0)
            {
                text.Text = step.Text;
                return;
            }

            if (!string.Equals(typed, step.Text, StringComparison.Ordinal))
            {
                _document.Execute(BoardCommands.SetStepText(step, typed));
            }
        }

        text.LostFocus += (_, _) => Commit();
        text.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        row.Children.Add(text);

        return row;
    }

    /// <summary>The three sections a task can grow, in the order the prototype's menu lists them.</summary>
    private static readonly (string Key, string Icon, string Name)[] Fields =
    [
        ("labels", "tag", "Labels"),
        ("due", "calendar", "Due date"),
        ("steps", "square-check", "Steps"),
    ];

    /// <summary>Whether <paramref name="key"/>'s section is on screen — because the task has it, or
    /// because you asked for it.</summary>
    private bool IsShown(string key) => key switch
    {
        "labels" => _task.Labels.Count > 0 || _revealed.Contains("labels"),
        "due" => _task.Due is not null || _revealed.Contains("due"),
        _ => _task.Steps.Count > 0 || _revealed.Contains("steps"),
    };

    /// <summary>
    /// One "+" that opens the whole field list, rather than a row of "+ Labels", "+ Due date", "+ Steps"
    /// buttons sitting permanently under the notes. Three buttons is three things to read every time you
    /// open a task, to add something to a task you usually don't; a "+" is one thing that says the same.
    /// <para>
    /// Notes are not in it: they are on by default, because a task's note is the field people reach for
    /// without being offered it. Fields the task already has stay listed and are marked "on", so the menu
    /// is a complete map of what a task can hold rather than a list that shrinks as you use it — a menu
    /// whose contents depend on the task is a menu you have to read.
    /// </para>
    /// </summary>
    private Avalonia.Controls.Control BuildAddButton()
    {
        var button = new Button
        {
            Content = new LucideIcon { IconKey = "plus", Size = 16 },
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(10, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Flyout = AddFieldMenu,
        };
        ToolTip.SetTip(button, "Add a field to this task");
        Avalonia.Automation.AutomationProperties.SetName(button, "Add a field to this task");
        return button;
    }

    /// <summary>The "+" menu itself. Its rows are rebuilt on every open, because which of them are
    /// already "on" is a fact about the task and the task is being edited underneath it.</summary>
    internal Flyout AddFieldMenu => field ??= BuildAddFieldMenu();

    private Flyout BuildAddFieldMenu()
    {
        var panel = new StackPanel { Spacing = 2, MinWidth = 186 };
        var flyout = new Flyout
        {
            Content = panel,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard,
        };

        flyout.Opening += (_, _) =>
        {
            var palette = Tokens.Active;
            panel.Children.Clear();
            panel.Children.Add(new TextBlock
            {
                Text = "ADD TO TASK",
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 0.9,
                Foreground = palette.MutedText,
                Margin = new Thickness(7, 5, 7, 4),
            });

            foreach (var (key, icon, name) in Fields)
            {
                panel.Children.Add(BuildFieldRow(key, icon, name, flyout));
            }
        };

        return flyout;
    }

    private Button BuildFieldRow(string key, string icon, string name, Flyout flyout)
    {
        var palette = Tokens.Active;
        var row = new DockPanel { LastChildFill = true };

        var glyph = new LucideIcon { IconKey = icon, Size = 14, Foreground = palette.MutedText };
        DockPanel.SetDock(glyph, Dock.Left);
        row.Children.Add(glyph);

        if (IsShown(key))
        {
            var on = new TextBlock
            {
                Text = "on",
                FontSize = 10,
                Foreground = palette.Accent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(on, Dock.Right);
            row.Children.Add(on);
        }

        row.Children.Add(new TextBlock
        {
            Text = name,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Content = row,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        Avalonia.Automation.AutomationProperties.SetHelpText(button, IsShown(key) ? "on" : null);

        button.Click += (_, _) =>
        {
            flyout.Hide();

            // Picking a field the task already shows is not a mistake to refuse — it is someone looking
            // for where that field went. Revealing it again costs nothing and scrolls it into view.
            _revealed.Add(key);
            RebuildOptional();
        };

        return button;
    }
}
