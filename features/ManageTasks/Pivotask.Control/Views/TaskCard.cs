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
    /// <summary>
    /// The height of the card's first line, and the box the checkbox and the star are each drawn in.
    ///
    /// <para>It is one number for all three on purpose. A stock <see cref="CheckBox"/> and a stock
    /// <see cref="Button"/> are 32px tall by default and centre their glyph in that, while a line of
    /// 13.5pt text is 16px — so top-aligning the row put the title six pixels above the two things
    /// beside it. Pinning all three to the same line box is what makes them agree, and it keeps the
    /// prototype's rule that a wrapped title grows <em>downward</em> away from a checkbox that stays on
    /// the first line, rather than dragging the checkbox to the middle of a paragraph.</para>
    /// </summary>
    private const double LineHeight = 20;

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
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Top, or the DockPanel stretches the slot to the whole row and a card with a footline centres
        // its tick box against the block rather than against the title's first line.
        var tick = new LineSlot
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 9, 0),
            Children = { done },
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
            FontSize = 13.5,
            LineHeight = LineHeight,
            VerticalAlignment = VerticalAlignment.Top,

            // A completed task keeps its words — you should still be able to read what you finished —
            // but stops competing for attention with the ones that aren't done.
            TextDecorations = task.Done ? Avalonia.Media.TextDecorations.Strikethrough : null,

            // Never null for the unfinished case. Foreground is an inherited property, and assigning null
            // is not "leave it alone" — it is a local value that stops the inheritance and hands the text
            // layout no brush at all, so the title drew nothing. Every card on the board was a checkbox
            // and a star with a blank between them.
            Foreground = task.Done ? palette.MutedText : palette.Text,
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

        // The same star the task dialog puts in its header — one control, so there is one answer to what
        // starred looks like rather than a drawn glyph in one place and a "★" character in the other.
        var star = StarButton.Build(task.Important, () => StarToggled?.Invoke(this, !task.Important));

        // A star only shows itself when it has something to say. Starring is rare and unstarring rarer, so
        // an outline drawn on every card in every column was a hollow shape repeated twenty times down the
        // board — noise the eye has to discard before it can find the two cards that *are* starred. Hidden
        // at rest and revealed under the pointer or the focus ring, which is the same bargain the group
        // header's × already makes: the affordance is still there, it simply isn't shouting.
        //
        // Opacity rather than IsVisible, so the card's first line keeps the same shape whether or not the
        // star is showing — a title that reflowed as the pointer crossed it would be a worse noise than
        // the one this removes. And the pointer is always over the card before it can be over the star, so
        // there is no moment where a click lands on something invisible.
        if (!task.Important)
        {
            star.Opacity = 0;
            PointerEntered += (_, _) => star.Opacity = 1;
            PointerExited += (_, _) => star.Opacity = 0;
            star.GotFocus += (_, _) => star.Opacity = 1; // reachable by keyboard, not only by pointer
            star.LostFocus += (_, _) => star.Opacity = 0;
        }

        var starSlot = new LineSlot
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 0, 0, 0),
            Children = { star },
        };

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tick, Dock.Left);
        DockPanel.SetDock(starSlot, Dock.Right);
        row.Children.Add(tick);
        row.Children.Add(starSlot);
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

    /// <summary>
    /// Holds one control on a single line of text: measured and drawn at the size it asks for, but taking
    /// only <see cref="LineHeight"/> of the row, centred.
    ///
    /// <para>This exists because you cannot simply set a stock control's Height to the line height. The
    /// Fluent CheckBox centres a 20px box inside a 32px row and clips to its own bounds, so a Height of 20
    /// cut the bottom off the box rather than moving it up — and wrapping it in a fixed-height panel does
    /// the same thing, because an explicit Height is passed down as a <em>measure constraint</em>. The
    /// child has to be measured against infinity and then placed, which is what this does. No arithmetic
    /// on the theme's numbers, so it keeps working if they change.</para>
    /// </summary>
    private sealed class LineSlot : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (Children.Count == 0)
            {
                return new Size(0, LineHeight);
            }

            var child = Children[0];
            child.Measure(Size.Infinity);
            return new Size(child.DesiredSize.Width, LineHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count > 0)
            {
                var child = Children[0];
                var wanted = child.DesiredSize;
                child.Arrange(new Rect(0, (finalSize.Height - wanted.Height) / 2, wanted.Width, wanted.Height));
            }

            return finalSize;
        }
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
