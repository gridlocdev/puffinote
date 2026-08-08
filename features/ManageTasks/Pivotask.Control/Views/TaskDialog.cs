using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
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
        _body.Children.Add(title);

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
        _body.Children.Add(BuildAddMenu());

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

        if (_task.Labels.Count > 0 || _revealed.Contains("labels"))
        {
            _optional.Children.Add(Section("Labels", BuildLabels()));
        }

        if (_task.Due is not null || _revealed.Contains("due"))
        {
            _optional.Children.Add(Section("Due", BuildDue()));
        }

        if (_task.Steps.Count > 0 || _revealed.Contains("steps"))
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

    /// <summary>The "+" menu: reveal a section the task doesn't use yet.</summary>
    private Avalonia.Controls.Control BuildAddMenu()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (key, label) in new[] { ("labels", "+ Labels"), ("due", "+ Due date"), ("steps", "+ Steps") })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                _revealed.Add(key);
                RebuildOptional();
            };
            row.Children.Add(button);
        }

        return row;
    }
}
