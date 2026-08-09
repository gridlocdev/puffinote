using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// One task, as a card: a checkbox, its title, and — only when it has them — label chips, a due pill,
/// a step count, and a star. The card shows nothing it has nothing to say about, which is what keeps a
/// column of plain one-line tasks from looking like a form.
/// </summary>
internal sealed class TaskCard : Border
{
    public TaskCard(Board board, TaskItem task, DateOnly today)
    {
        Task = task;
        var palette = Tokens.Active;

        Background = palette.Surface;
        BorderBrush = palette.SurfaceBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(10, 8);
        Margin = new Thickness(0, 0, 0, 6);
        Cursor = new Cursor(StandardCursorType.Hand);

        var done = new CheckBox
        {
            IsChecked = task.Done,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -2, 6, 0),
        };
        done.IsCheckedChanged += (_, _) =>
        {
            if (done.IsChecked != task.Done)
            {
                DoneToggled?.Invoke(this, done.IsChecked == true);
            }
        };

        var title = new TextBlock
        {
            Text = task.Title,
            TextWrapping = TextWrapping.Wrap,

            // A completed task keeps its words — you should still be able to read what you finished —
            // but stops competing for attention with the ones that aren't done.
            TextDecorations = task.Done ? Avalonia.Media.TextDecorations.Strikethrough : null,
            Foreground = task.Done ? palette.MutedText : null,
        };

        var meta = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (string id in task.Labels)
        {
            if (board.FindLabel(id) is { } label)
            {
                meta.Children.Add(Chip(label.Name, ParseColor(label.Color)));
            }
        }

        if (task.Due is { } due)
        {
            // Overdue is the one state on a card that should be able to interrupt you, so it is the one
            // that gets a colour of its own rather than the chrome's muted grey.
            bool late = !task.Done && due < today;
            meta.Children.Add(Chip(BoardQuery.DescribeDue(due, today), late ? Color.FromRgb(0xBE, 0x12, 0x3C) : null));
        }

        var (stepsDone, stepsTotal) = task.StepProgress;
        if (stepsTotal > 0)
        {
            // "1/3 steps", not a bare "1/3": the card's other facts are self-describing (a label says its
            // own name, a due pill says a date) and a lone fraction beside them is a riddle.
            meta.Children.Add(Chip($"{stepsDone}/{stepsTotal} steps", null));
        }

        // A note is a fact about the task like any other, and the one the card cannot show: it says the
        // note exists so you know there is something behind the title worth opening.
        if (!string.IsNullOrWhiteSpace(task.Notes))
        {
            meta.Children.Add(Chip("note", null));
        }

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(title);
        if (meta.Children.Count > 0)
        {
            stack.Children.Add(meta);
        }

        var star = new Button
        {
            Content = task.Important ? "★" : "☆",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = task.Important ? Brushes.Goldenrod : palette.MutedText,
        };
        star.Click += (_, _) => StarToggled?.Invoke(this, !task.Important);

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(done, Dock.Left);
        DockPanel.SetDock(star, Dock.Right);
        row.Children.Add(done);
        row.Children.Add(star);
        row.Children.Add(stack);
        Child = row;

        // A single click opens the task, as it does in the prototype. The card is a summary of something
        // there is more of — opening it is the only reason to click one, and the checkbox and star are
        // separate targets that handle their own clicks, so there is nothing a plain click could mean
        // instead. Focusable so the same thing is reachable from the keyboard.
        Focusable = true;
        Tapped += (_, e) =>
        {
            // The checkbox and the star are their own targets sitting inside the card. A tap that landed
            // on one of those has already meant something, and must not also open the task.
            if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
            {
                return;
            }

            // A drag that ended where it started is still a drag, and Avalonia reports it as a tap. The
            // person who put a card down did not also ask to open it.
            if (Dragging)
            {
                return;
            }

            Opened?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                Opened?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
    }

    public TaskItem Task { get; }

    /// <summary>
    /// Set by <see cref="CardDrag"/> while this card is being dragged, and cleared when the next press
    /// starts a fresh gesture. It exists so the release that ends a drag doesn't also read as a click.
    /// </summary>
    internal bool Dragging { get; set; }

    public event EventHandler<bool>? DoneToggled;

    public event EventHandler<bool>? StarToggled;

    /// <summary>Raised on a click, or Enter/Space — the card's "tell me everything" gesture.</summary>
    public event EventHandler? Opened;

    /// <summary>A small rounded pill. The label chip and the due pill are the same shape on purpose:
    /// they are both "one fact about this task", and inventing a second shape would imply otherwise.</summary>
    internal static Border Chip(string text, Color? accent)
    {
        var palette = Tokens.Active;
        return new Border
        {
            Background = accent is { } color ? new SolidColorBrush(color, 0.22) : palette.SurfaceInset,
            BorderBrush = accent is { } edge ? new SolidColorBrush(edge, 0.55) : palette.SurfaceBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 1),
            Margin = new Thickness(0, 0, 4, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = accent is { } ink ? new SolidColorBrush(Lighten(ink)) : palette.MutedText,
            },
        };
    }

    /// <summary>Parse an <c>#RRGGBB</c> label colour, falling back to a neutral rather than throwing —
    /// a hand-edited board with a typo in a colour should still open.</summary>
    private static Color ParseColor(string hex) =>
        Color.TryParse(hex, out var color) ? color : Color.FromRgb(0x64, 0x74, 0x8B);

    /// <summary>
    /// The label palette in the prototype was chosen for white cards. On the dark chrome these controls
    /// live on, those same colours are too dark to read as text, so a chip's ink is a lightened form of
    /// its colour while its fill stays the colour itself.
    /// </summary>
    private static Color Lighten(Color color) => Color.FromRgb(
        (byte)Math.Min(255, color.R + 90),
        (byte)Math.Min(255, color.G + 90),
        (byte)Math.Min(255, color.B + 90));
}
