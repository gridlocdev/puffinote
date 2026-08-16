using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// Somewhere a card can be dropped: the group it would join, the frame that lights up to say so, and the
/// panel its cards are stacked in. A board column and a list section are the same three things, which is
/// why one drag controller serves both views.
/// </summary>
/// <param name="Group">The group a card dropped here joins.</param>
/// <param name="Frame">The bordered box that lights while the pointer is over this zone.</param>
/// <param name="Cards">The panel holding this zone's <see cref="TaskCard"/>s, and the insertion marker.</param>
internal sealed record DropZone(TaskGroup Group, Border Frame, Panel Cards);

/// <summary>
/// Dragging a card from one place on the board to another.
///
/// <para>Pointer-driven rather than <see cref="DragDrop"/>: the platform's drag loop is a modal
/// OS affair that can't be driven headlessly, and this needs to be testable — proposal 0004 §7 is
/// explicit that the keyboard/menu path is the one that must work, so drag has to be the part that can
/// be proven rather than the part everything else waits on.</para>
///
/// <para>A press alone is not a drag. The gesture only becomes one past a few pixels of travel, which is
/// what leaves a plain click free to mean "open this task"; until then nothing has happened and nothing
/// is shown. Once it is a drag, the card lifts onto the cursor, dims where it came from, and a line shows
/// where it would land, because a drag with no answer to "where exactly?" is a drag you have to undo to
/// find out.</para>
/// </summary>
/// <param name="root">The view the gesture's coordinates are measured in.</param>
/// <param name="document">The open document, read late so a rebind is picked up.</param>
/// <param name="ghosts">The hit-test-invisible layer over the view that the lifted card is drawn on.</param>
/// <param name="lightZones">Whether the zone under the pointer lights its own border. True on the board,
/// where a column is a bordered box and lighting it is a change of colour; false in the list, where a
/// section has no border of its own and this would <em>grow</em> one under the pointer.</param>
internal sealed class CardDrag(Visual root, Func<BoardDocument> document, Canvas ghosts, bool lightZones = true)
{
    /// <summary>How far the pointer travels before a press becomes a drag rather than a click.</summary>
    private const double Threshold = 4;

    /// <summary>
    /// How far the lifted card is tilted, in degrees. Small on purpose: enough to say "this one is off the
    /// board", not enough to make the title harder to read than the cards it is passing over. It is the
    /// tilt every board of this shape uses, for the same reason.
    /// </summary>
    private const double Tilt = 2.5;

    /// <summary>The zones currently on screen. A view clears this and refills it on every rebuild.</summary>
    public List<DropZone> Zones { get; } = [];

    private TaskCard? _card;
    private TaskGroup? _origin;
    private Point _start;
    private bool _armed;

    private Border? _marker;
    private TaskCard? _ghost;

    /// <summary>Where inside the card the pointer took hold of it, so the ghost stays under the same
    /// point of the card it was picked up by rather than jumping its own centre to the cursor.</summary>
    private Point _grab;

    private DropZone? _lit;
    private (IBrush? Brush, Thickness Width) _litWas;
    private DropZone? _target;
    private TaskItem? _before;

    /// <summary>Whether a drag is in flight — past the threshold, with a card lifted and a line drawn.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    /// Abandon a drag in flight and leave the board exactly as it was: the marker goes, the column stops
    /// glowing, the card comes back to full opacity, and nothing is executed. Returns whether there was a
    /// drag to abandon.
    /// <para>
    /// A drag you have started and thought better of otherwise had only one way out — drop it back exactly
    /// where it came from and hope the no-op check catches it. That is a guess, not an escape hatch, and it
    /// is not available at all once you have moved the pointer to another column.
    /// </para>
    /// <para>
    /// <see cref="TaskCard.Dragging"/> is deliberately left set, as after a drop: the button is still down,
    /// and the release that ends this gesture must not be read as a click that opens the task.
    /// </para>
    /// </summary>
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

    /// <summary>Make <paramref name="card"/> draggable out of <paramref name="group"/>.</summary>
    public void Attach(TaskCard card, TaskGroup group)
    {
        card.PointerPressed += (_, e) =>
        {
            card.Dragging = false;
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                return;
            }

            // The checkbox and the star are their own targets. A press that landed on one is on its way
            // to meaning something else, and must not also arm a drag of the card underneath it.
            if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
            {
                return;
            }

            _card = card;
            _origin = group;
            _start = e.GetPosition(root);
            _armed = true;
            IsDragging = false;
        };

        card.PointerMoved += (_, e) =>
        {
            if (!_armed || !ReferenceEquals(_card, card))
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

                Begin(card, e.Pointer);
            }

            Track(at);
        };

        card.PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(_card, card))
            {
                return;
            }

            if (IsDragging)
            {
                Finish();
                e.Handled = true;
            }

            Reset();
        };

        // Capture is lost when the rebuild after a drop tears the card down, and when another control
        // takes the pointer. Either way the gesture is over and nothing should be left lit.
        card.PointerCaptureLost += (_, _) =>
        {
            if (ReferenceEquals(_card, card))
            {
                Clear();
                Reset();
            }
        };
    }

    private void Begin(TaskCard card, IPointer pointer)
    {
        IsDragging = true;
        pointer.Capture(card);

        // Dimmed rather than hidden: the gap it leaves is the answer to "where does it go back to if I
        // change my mind", which a card that vanished would have thrown away.
        card.Opacity = 0.35;
        card.Dragging = true;
        Lift(card);
    }

    /// <summary>
    /// Put a copy of the card in the air, under the cursor.
    ///
    /// <para>Without this the only thing a drag showed was a dimmed gap and a two-pixel line, and the card
    /// itself stayed exactly where it was — so the gesture read as "nothing is happening" right up until
    /// the drop, and what was being moved was something you had to remember rather than something you
    /// could see. It lifts (a shadow), tilts a couple of degrees, and follows the pointer; the original
    /// stays put as the dimmed placeholder that says where it would go back to.</para>
    ///
    /// <para>A real <see cref="TaskCard"/> built from the same task, not a drawn approximation of one, so
    /// the thing in the air carries the same chips and the same due pill as the thing being moved — and so
    /// there is no second card design to keep in step with the first.</para>
    /// </summary>
    private void Lift(TaskCard card)
    {
        _grab = _start - BoundsIn(card).Position;

        var ghost = card.Duplicate();
        ghost.Width = card.Bounds.Width;
        ghost.Margin = default; // the original's bottom gap is spacing between cards, not part of one
        ghost.Opacity = 0.9;
        ghost.IsHitTestVisible = false;
        ghost.RenderTransform = new RotateTransform(Tilt);
        ghost.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetY = 10,
            Blur = 24,
            Color = Color.FromArgb(0x4D, 0, 0, 0),
        });

        _ghost = ghost;
        ghosts.Children.Add(ghost);
    }

    private void Track(Point at)
    {
        if (_ghost is { } ghost)
        {
            Canvas.SetLeft(ghost, at.X - _grab.X);
            Canvas.SetTop(ghost, at.Y - _grab.Y);
        }

        var zone = Nearest(at);
        if (zone is null)
        {
            return;
        }

        RemoveMarker();
        if (!ReferenceEquals(_lit, zone))
        {
            Unlight();
            Light(zone);
        }

        var (index, before) = Slot(zone, at);
        _marker ??= NewMarker();
        zone.Cards.Children.Insert(index, _marker);
        _target = zone;
        _before = before;
    }

    private void Finish()
    {
        if (_target is not { } zone || _card is not { } card || _origin is not { } origin)
        {
            return;
        }

        var task = card.Task;
        var target = zone.Group;
        Clear();

        // What the drop would produce, compared with what is already there. A card dropped back where it
        // came from is not an edit, and an undo entry that reverses nothing is worse than none: it makes
        // ⌘Z lie about how far back the last real change was.
        var next = target.Tasks.Where(t => !ReferenceEquals(t, task)).ToList();
        int at = _before is null ? next.Count : Math.Max(0, next.IndexOf(_before));
        next.Insert(at, task);
        if (ReferenceEquals(origin, target) && next.SequenceEqual(target.Tasks))
        {
            return;
        }

        var open = document();
        open.Execute(BoardCommands.MoveTask(open.Board, task, target, _before));
    }

    /// <summary>Where in <paramref name="zone"/> the pointer is pointing: the child slot for the marker,
    /// and the task the dragged one would land in front of (null for the end).</summary>
    private (int Index, TaskItem? Before) Slot(DropZone zone, Point at)
    {
        var children = zone.Cards.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not TaskCard card || ReferenceEquals(card, _card))
            {
                continue;
            }

            var box = BoundsIn(card);

            // Past a card's midpoint means below it, which is how a drop lands *after* the last card
            // rather than only ever before one.
            if (at.Y < box.Y + (box.Height / 2))
            {
                return (i, card.Task);
            }
        }

        return (children.Count, null);
    }

    /// <summary>
    /// The zone the pointer is in, or — when it is in none — the nearest one. Nearest rather than
    /// strictly inside, because columns are only as tall as their tasks: the room below a short column is
    /// unmistakably that column's, and a drop there that did nothing would read as a broken drag.
    /// </summary>
    private DropZone? Nearest(Point at)
    {
        DropZone? best = null;
        double nearest = double.MaxValue;
        foreach (var zone in Zones)
        {
            double distance = Distance(BoundsIn(zone.Frame), at);
            if (distance < nearest)
            {
                nearest = distance;
                best = zone;
            }
        }

        return best;
    }

    private Rect BoundsIn(Visual visual) =>
        visual.TranslatePoint(default, root) is { } origin
            ? new Rect(origin, visual.Bounds.Size)
            : default;

    private static double Distance(Rect rect, Point point)
    {
        double dx = Math.Max(0, Math.Max(rect.X - point.X, point.X - rect.Right));
        double dy = Math.Max(0, Math.Max(rect.Y - point.Y, point.Y - rect.Bottom));
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Border NewMarker() => new()
    {
        Height = 2,
        Margin = new Thickness(2, 2),
        CornerRadius = new CornerRadius(1),
        Background = Tokens.Active.Accent,
    };

    private void RemoveMarker()
    {
        if (_marker is { Parent: Panel panel })
        {
            panel.Children.Remove(_marker);
        }
    }

    private void Light(DropZone zone)
    {
        if (!lightZones)
        {
            return;
        }

        _lit = zone;
        _litWas = (zone.Frame.BorderBrush, zone.Frame.BorderThickness);
        zone.Frame.BorderBrush = Tokens.Active.Accent;
        zone.Frame.BorderThickness = new Thickness(1);
    }

    private void Unlight()
    {
        if (_lit is { } zone)
        {
            zone.Frame.BorderBrush = _litWas.Brush;
            zone.Frame.BorderThickness = _litWas.Width;
            _lit = null;
        }
    }

    /// <summary>Take back everything the drag drew, so a rebuild starts from an unmarked board.</summary>
    private void Clear()
    {
        RemoveMarker();
        Unlight();
        _marker = null;

        if (_ghost is { } ghost)
        {
            ghosts.Children.Remove(ghost);
            _ghost = null;
        }

        if (_card is { } card)
        {
            card.Opacity = 1;
        }
    }

    /// <summary>End the gesture. <see cref="TaskCard.Dragging"/> is deliberately left set: the tap that
    /// follows this release is the one that must not also open the task.</summary>
    private void Reset()
    {
        _card = null;
        _origin = null;
        _target = null;
        _before = null;
        _armed = false;
        IsDragging = false;
    }
}
