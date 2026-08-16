using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>One group on screen: the group itself, and the box the whole of it is drawn in.</summary>
internal sealed record GroupZone(TaskGroup Group, Border Frame);

/// <summary>
/// Dragging a group's <em>heading</em> to move the whole group — the board's columns left and right, the
/// list's sections up and down.
///
/// <para>Before this, the order of the columns was whatever order they had been created in, permanently:
/// the only way to put "This week" ahead of "Inbox" was to rename both, or to move every task in one of
/// them. A board's columns are usually a sequence — a pipeline, a week, a set of stages — and a sequence
/// you cannot reorder is a sequence you have to get right the first time.</para>
///
/// <para>The same gesture grammar as <see cref="CardDrag"/>, deliberately: nothing happens until the
/// pointer has travelled a few pixels, so a plain click on a heading is still the rename it always was;
/// past that the group dims where it stands, its name rides under the cursor, and a bar shows the slot it
/// would drop into. Two different rules for "how do I move this thing" on one board would be one rule
/// too many.</para>
/// </summary>
/// <param name="root">The view the gesture's coordinates are measured in.</param>
/// <param name="document">The open document, read late so a rebind is picked up.</param>
/// <param name="ghosts">The hit-test-invisible layer the heading in flight is drawn on.</param>
/// <param name="lane">The panel the groups are stacked in, and where the insertion bar is placed.</param>
/// <param name="horizontal">Whether the groups run left-to-right (the board) or top-to-bottom (the list).</param>
internal sealed class GroupDrag(
    Visual root, Func<BoardDocument> document, Canvas ghosts, Panel lane, bool horizontal)
{
    /// <summary>How far the pointer travels before a press on a heading stops being a click. Shared with
    /// <see cref="GroupHeader"/>, whose rename is the other thing a press on a heading can mean.</summary>
    public const double Threshold = 4;

    /// <summary>The groups currently on screen, in board order. Refilled on every rebuild.</summary>
    public List<GroupZone> Zones { get; } = [];

    private TaskGroup? _group;
    private Border? _frame;
    private Point _start;
    private Point _grab;
    private bool _armed;

    private Border? _marker;
    private Border? _ghost;
    private TaskGroup? _before;

    /// <summary>Whether a group is in the air.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Make <paramref name="heading"/> the handle that drags <paramref name="group"/>'s
    /// <paramref name="frame"/>, and register the frame as somewhere another group can be dropped beside.</summary>
    public void Attach(Avalonia.Controls.Control heading, TaskGroup group, Border frame)
    {
        Zones.Add(new GroupZone(group, frame));

        heading.Cursor = new Cursor(StandardCursorType.SizeAll);

        // handledEventsToo throughout: the name inside the heading marks its own press handled so the
        // column underneath doesn't read it as a click on itself, and that press is still the start of
        // this gesture.
        heading.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        heading.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        heading.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        heading.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);

        void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(heading).Properties.IsLeftButtonPressed)
            {
                return;
            }

            // The ✕ and the fold caret are their own targets, and a heading already being renamed is a
            // text box you are typing in — neither is a handle for moving the group.
            if (e.Source is Visual source
                && (source.FindAncestorOfType<Button>(includeSelf: true) is not null
                    || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null))
            {
                return;
            }

            _group = group;
            _frame = frame;
            _start = e.GetPosition(root);
            _armed = true;
            IsDragging = false;
        }

        void OnMoved(object? sender, PointerEventArgs e)
        {
            if (!_armed || !ReferenceEquals(_group, group))
            {
                return;
            }

            var at = e.GetPosition(root);
            if (!IsDragging)
            {
                if (Math.Abs(at.X - _start.X) < Threshold && Math.Abs(at.Y - _start.Y) < Threshold)
                {
                    return;
                }

                Begin(heading, group, e.Pointer);
            }

            Track(at);
        }

        void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!ReferenceEquals(_group, group))
            {
                return;
            }

            if (IsDragging)
            {
                Finish();
                e.Handled = true;
            }

            Reset();
        }

        void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (ReferenceEquals(_group, group))
            {
                Clear();
                Reset();
            }
        }
    }

    /// <summary>Abandon a move in flight, leaving the board exactly as it was; false when there was none.</summary>
    public bool Cancel()
    {
        if (!IsDragging)
        {
            return false;
        }

        Clear();
        Reset();
        return true;
    }

    private void Begin(Avalonia.Controls.Control heading, TaskGroup group, IPointer pointer)
    {
        IsDragging = true;
        pointer.Capture(heading);

        // Dimmed, not hidden, for the reason a dragged card is: the gap it leaves is the answer to
        // "where does this go back to if I change my mind".
        if (_frame is { } frame)
        {
            frame.Opacity = 0.35;
        }

        _grab = _start - BoundsIn(heading).Position;
        _ghost = NameGhost(group, heading.Bounds.Width);
        ghosts.Children.Add(_ghost);
    }

    private void Track(Point at)
    {
        if (_ghost is { } ghost)
        {
            Canvas.SetLeft(ghost, at.X - _grab.X);
            Canvas.SetTop(ghost, at.Y - _grab.Y);
        }

        RemoveMarker();
        var (index, before) = Slot(at);
        _marker ??= NewMarker();
        lane.Children.Insert(index, _marker);
        _before = before;
    }

    /// <summary>Where in the lane the pointer is pointing: the child slot for the bar, and the group the
    /// dragged one would land in front of (null for the end).</summary>
    private (int Index, TaskGroup? Before) Slot(Point at)
    {
        int last = 0;
        for (int i = 0; i < lane.Children.Count; i++)
        {
            if (lane.Children[i] is not Border frame
                || Zones.Find(z => ReferenceEquals(z.Frame, frame)) is not { } zone)
            {
                continue;
            }

            last = i + 1;
            if (ReferenceEquals(zone.Group, _group))
            {
                continue;
            }

            var box = BoundsIn(frame);

            // Past a group's midpoint means after it, which is how a drop lands at the far end of the
            // lane rather than only ever in front of something.
            double middle = horizontal ? box.X + (box.Width / 2) : box.Y + (box.Height / 2);
            if ((horizontal ? at.X : at.Y) < middle)
            {
                return (i, zone.Group);
            }
        }

        // After the last group, but before the trailing "add a list" — that stub marks the slot after the
        // board, and a column dropped past the end belongs in the board, not after the invitation to grow it.
        return (last, null);
    }

    private void Finish()
    {
        if (_group is not { } group)
        {
            return;
        }

        var before = _before;
        Clear();

        var open = document();
        var order = open.Board.Groups.Where(g => !ReferenceEquals(g, group)).ToList();
        int at = before is null ? order.Count : Math.Max(0, order.IndexOf(before));
        order.Insert(at, group);

        // A group dropped back where it came from is not an edit, and an undo entry that reverses nothing
        // makes ⌘Z lie about how far back the last real change was.
        if (order.SequenceEqual(open.Board.Groups))
        {
            return;
        }

        open.Execute(BoardCommands.MoveGroup(open.Board, group, before));
    }

    /// <summary>The heading in flight: the group's name, as the heading writes it, on a floating card.</summary>
    private static Border NameGhost(TaskGroup group, double width)
    {
        var palette = Tokens.Active;
        return new Border
        {
            Width = Math.Max(width, 80),
            Background = palette.Surface,
            BorderBrush = palette.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6),
            Opacity = 0.9,
            IsHitTestVisible = false,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 10, Blur = 24, Color = Color.FromArgb(0x4D, 0, 0, 0) }),
            Child = new TextBlock
            {
                Text = group.Name.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
                FontWeight = FontWeight.SemiBold,
                FontSize = 12.5,
                LetterSpacing = 0.7,
                Foreground = palette.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
    }

    /// <summary>The bar marking the slot: across the lane, at right angles to the way the groups run.</summary>
    private Border NewMarker() => horizontal
        ? new Border
        {
            Width = 3,
            Margin = new Thickness(2, 0),
            CornerRadius = new CornerRadius(2),
            Background = Tokens.Active.Accent,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 60,
        }
        : new Border
        {
            Height = 3,
            Margin = new Thickness(0, 2),
            CornerRadius = new CornerRadius(2),
            Background = Tokens.Active.Accent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    private void RemoveMarker()
    {
        if (_marker is { Parent: Panel panel })
        {
            panel.Children.Remove(_marker);
        }
    }

    private Rect BoundsIn(Visual visual) =>
        visual.TranslatePoint(default, root) is { } origin
            ? new Rect(origin, visual.Bounds.Size)
            : default;

    /// <summary>Take back everything the drag drew, so a rebuild starts from an unmarked board.</summary>
    private void Clear()
    {
        RemoveMarker();
        _marker = null;

        if (_ghost is { } ghost)
        {
            ghosts.Children.Remove(ghost);
            _ghost = null;
        }

        if (_frame is { } frame)
        {
            frame.Opacity = 1;
        }
    }

    private void Reset()
    {
        _group = null;
        _frame = null;
        _before = null;
        _armed = false;
        IsDragging = false;
    }
}
