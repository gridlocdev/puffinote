using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
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
    private readonly StackPanel _body = new() { Spacing = 12, Width = 420 };
    private readonly StackPanel _optional = new() { Spacing = 12 };

    public TaskDialog(BoardDocument document, TaskItem task)
    {
        _document = document;
        _task = task;
        Title = "Task";

        var palette = Tokens.Active;

        var title = new TextBox { Text = task.Title, AcceptsReturn = false };
        title.LostFocus += (_, _) =>
        {
            if ((title.Text ?? string.Empty) != task.Title)
            {
                _document.Execute(BoardCommands.SetTitle(task, title.Text ?? string.Empty));
            }
        };

        // The "+" sits on the title row, beside the one thing every task has, because what it offers is
        // the rest of what this task could be.
        var titleRow = new DockPanel { LastChildFill = true };
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
            MinHeight = 70,

            // Plain text, deliberately (proposal 0004 §10.4). A note attached to a task is a sentence or
            // two; if it genuinely wants to be a document, that document is a .md file.
            PlaceholderText = "Notes",
        };
        notes.LostFocus += (_, _) =>
        {
            if ((notes.Text ?? string.Empty) != task.Notes)
            {
                _document.Execute(BoardCommands.SetNotes(task, notes.Text ?? string.Empty));
            }
        };
        _body.Children.Add(notes);

        _body.Children.Add(_optional);

        var close = new Button { Content = "Done", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close(true);
        _body.Children.Add(close);

        DialogContent = _body;
        RebuildOptional();
        _ = palette;
    }

    /// <summary>Which optional sections the user has asked for, on top of the ones the task already has.</summary>
    private readonly HashSet<string> _revealed = [];

    private void RebuildOptional()
    {
        _optional.Children.Clear();

        if (IsShown("labels"))
        {
            _optional.Children.Add(Section("Labels", BuildLabels()));
        }

        if (IsShown("due"))
        {
            _optional.Children.Add(Section("Due", BuildDue()));
        }

        if (IsShown("steps"))
        {
            _optional.Children.Add(Section("Steps", BuildSteps()));
        }
    }

    private static Avalonia.Controls.Control Section(string heading, Avalonia.Controls.Control body) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = heading, FontSize = 11, Foreground = Tokens.Active.MutedText },
            body,
        },
    };

    private Avalonia.Controls.Control BuildLabels()
    {
        var panel = new WrapPanel();
        foreach (var label in _document.Board.Labels)
        {
            bool on = _task.Labels.Contains(label.Id, StringComparer.Ordinal);
            var toggle = new ToggleButton
            {
                Content = label.Name,
                IsChecked = on,
                Margin = new Thickness(0, 0, 4, 4),
            };
            toggle.IsCheckedChanged += (_, _) =>
            {
                if ((toggle.IsChecked == true) != _task.Labels.Contains(label.Id, StringComparer.Ordinal))
                {
                    _document.Execute(BoardCommands.ToggleLabel(_task, label.Id));
                }
            };
            panel.Children.Add(toggle);
        }

        if (_document.Board.Labels.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "This board has no labels yet.", Foreground = Tokens.Active.MutedText });
        }

        return panel;
    }

    private Avalonia.Controls.Control BuildDue()
    {
        var box = new TextBox
        {
            Text = _task.Due is { } due ? Ids.Format(due) : string.Empty,
            PlaceholderText = "YYYY-MM-DD",
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        box.LostFocus += (_, _) =>
        {
            // A date or nothing. No parsing of "next tuesday": this format has no times and no
            // timezones, and a guess that lands a day out is worse than a box that says no.
            var parsed = Ids.ParseDate(box.Text);
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                if (_task.Due is not null)
                {
                    _document.Execute(BoardCommands.SetDue(_task, null));
                }
            }
            else if (parsed is { } date && date != _task.Due)
            {
                _document.Execute(BoardCommands.SetDue(_task, date));
            }
            else if (parsed is null)
            {
                box.Text = _task.Due is { } current ? Ids.Format(current) : string.Empty;
            }
        };

        return box;
    }

    private Avalonia.Controls.Control BuildSteps()
    {
        var panel = new StackPanel { Spacing = 4 };

        var (done, total) = _task.StepProgress;
        if (total > 0)
        {
            panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = total, Value = done, Height = 4 });
        }

        foreach (var step in _task.Steps)
        {
            var check = new CheckBox { Content = step.Text, IsChecked = step.Done };
            check.IsCheckedChanged += (_, _) =>
            {
                if ((check.IsChecked == true) != step.Done)
                {
                    _document.Execute(BoardCommands.SetStepDone(step, check.IsChecked == true));
                    RebuildOptional(); // the progress bar moved
                }
            };
            panel.Children.Add(check);
        }

        var add = new TextBox { PlaceholderText = "Add a step" };
        add.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && !string.IsNullOrWhiteSpace(add.Text))
            {
                _document.Execute(BoardCommands.AddStep(_task, add.Text.Trim()));
                RebuildOptional();
                e.Handled = true;
            }
        };
        panel.Children.Add(add);

        if (_task.Steps.Count > 0)
        {
            var clear = new Button { Content = "Clear steps", HorizontalAlignment = HorizontalAlignment.Left };
            clear.Click += (_, _) =>
            {
                // One command, so one undo puts the whole checklist back.
                _document.Execute(BoardCommands.ClearSteps(_task));
                RebuildOptional();
            };
            panel.Children.Add(clear);
        }

        return panel;
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
