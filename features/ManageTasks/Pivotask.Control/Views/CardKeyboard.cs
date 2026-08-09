using Avalonia.Input;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;

namespace Pivotask.Control.Views;

/// <summary>
/// Getting around the board without a mouse, and moving a task without one.
///
/// <para>Proposal 0004 §7 is explicit that the keyboard path is the one that must work — drag is the
/// affordance, not the mechanism — and until now there wasn't one: <c>BoardCommands.MoveTask</c> existed
/// and nothing reached it except a drag. A board you can only rearrange by dragging is a board somebody
/// cannot rearrange at all.</para>
///
/// <para>Arrows move the <em>focus</em>; ⌥⇧ and an arrow move the <em>task</em>. That is the family's
/// existing idiom, not a new one — it is what Cellestra's grid does to a cell's contents and what the
/// radial settings list does to a segment — so the one chord anyone has to learn here is one they have
/// already learned somewhere else in the family.</para>
///
/// <para>Driven off the same <see cref="DropZone"/> list the drag controller uses, so the columns the
/// keyboard walks and the columns a card can be dropped into cannot drift apart.</para>
/// </summary>
/// <param name="document">The open board, resolved late — a rebuild replaces it.</param>
/// <param name="zones">The columns (or list sections) currently on screen, in visual order.</param>
/// <param name="sideBySide">
/// True when the zones are laid out across the screen (the board). False when they are stacked down it
/// (the list), where ←/→ mean nothing and ↑/↓ run through the whole list rather than stopping at a
/// section's edge — in a single column of sections, the card below the last of one section is the first
/// of the next, and stopping there would be stopping in the middle of what the reader sees as one list.
/// </param>
internal sealed class CardKeyboard(Func<BoardDocument> document, IReadOnlyList<DropZone> zones, bool sideBySide)
{
    /// <summary>Where a card sits: which zone, and which position among that zone's visible cards.</summary>
    private readonly record struct Spot(int Zone, int Index);

    /// <summary>What a key press turned out to be.</summary>
    public enum Outcome
    {
        /// <summary>Not ours — an arrow we could not use stays available to the scroll viewer.</summary>
        Ignored,

        /// <summary>The focus moved. Nothing was edited and nothing will be rebuilt.</summary>
        Navigated,

        /// <summary>The task moved. The view rebuilds, so the focus has to be put back afterwards.</summary>
        Moved,
    }

    /// <summary>Handle a key pressed on <paramref name="card"/>.</summary>
    public Outcome Handle(KeyEventArgs e, TaskCard card)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Find(card) is not { } spot)
        {
            return Outcome.Ignored;
        }

        // ⌥⇧ is "move the thing", plain is "move the cursor". Anything else — a bare ⌥, a ⌘ — belongs to
        // the host's command catalog and is not ours to take.
        bool moving = e.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift);
        if (e.KeyModifiers != KeyModifiers.None && !moving)
        {
            return Outcome.Ignored;
        }

        var (dx, dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };

        if (dx == 0 && dy == 0)
        {
            return Outcome.Ignored;
        }

        return moving
            ? (Move(card, spot, dx, dy) ? Outcome.Moved : Outcome.Ignored)
            : (FocusNeighbour(spot, dx, dy) ? Outcome.Navigated : Outcome.Ignored);
    }

    /// <summary>The card the keyboard should land on after a rebuild, or null when there is none.</summary>
    public TaskCard? CardFor(TaskItem task) =>
        zones.SelectMany(z => z.Cards.Children.OfType<TaskCard>())
            .FirstOrDefault(c => ReferenceEquals(c.Task, task));

    private Spot? Find(TaskCard card)
    {
        for (int z = 0; z < zones.Count; z++)
        {
            var cards = Cards(z);
            int index = cards.IndexOf(card);
            if (index >= 0)
            {
                return new Spot(z, index);
            }
        }

        return null;
    }

    private List<TaskCard> Cards(int zone) => [.. zones[zone].Cards.Children.OfType<TaskCard>()];

    /// <summary>Move the focus. Returns false when there is nowhere to go, so the key stays unhandled.</summary>
    private bool FocusNeighbour(Spot from, int dx, int dy)
    {
        if (!sideBySide)
        {
            // One column of sections: the whole thing reads as a single list, so walk it as one.
            if (dx != 0)
            {
                return false;
            }

            var flat = zones.SelectMany(z => z.Cards.Children.OfType<TaskCard>()).ToList();
            int at = flat.FindIndex(c => ReferenceEquals(c, Cards(from.Zone)[from.Index]));
            return Focus(flat, at + dy);
        }

        if (dy != 0)
        {
            // Within the column. A column's ends are ends: ↓ off the bottom of one column landing at the
            // top of the next would be reading order, but on a board nobody reads columns that way.
            return Focus(Cards(from.Zone), from.Index + dy);
        }

        // Across columns, landing as close to the same height as the neighbour has cards for. Empty
        // columns are stepped over rather than swallowing the focus into nowhere.
        for (int z = from.Zone + dx; z >= 0 && z < zones.Count; z += dx)
        {
            var cards = Cards(z);
            if (cards.Count > 0)
            {
                return Focus(cards, Math.Min(from.Index, cards.Count - 1));
            }
        }

        return false;
    }

    private static bool Focus(List<TaskCard> cards, int index)
    {
        if (index < 0 || index >= cards.Count)
        {
            return false;
        }

        // Directional, so the focus ring is drawn: this is keyboard navigation, and a card that has
        // focus without showing it is a cursor you cannot see.
        cards[index].Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>
    /// Move the task itself. Vertically it changes places with its neighbour in the same group;
    /// horizontally it joins the group beside it, at the same height. Every move goes through the same
    /// undoable <see cref="BoardCommands.MoveTask"/> a drop does, so ⌘Z reverses either identically.
    /// </summary>
    private bool Move(TaskCard card, Spot from, int dx, int dy)
    {
        var open = document();
        var task = card.Task;

        if (dy != 0)
        {
            var cards = Cards(from.Zone);
            int to = from.Index + dy;
            if (to < 0 || to >= cards.Count)
            {
                return false; // a card at the end of its group has nowhere further to go
            }

            // Moving down means landing *after* the card below, which is "before the one after that".
            var group = zones[from.Zone].Group;
            var before = dy > 0
                ? (to + 1 < cards.Count ? cards[to + 1].Task : null)
                : cards[to].Task;
            open.Execute(BoardCommands.MoveTask(open.Board, task, group, before));
            return true;
        }

        int target = from.Zone + dx;
        if (!sideBySide || target < 0 || target >= zones.Count)
        {
            return false;
        }

        // Into the neighbouring group at the same height, or at its end when it is shorter.
        var into = Cards(target);
        var landsBefore = from.Index < into.Count ? into[from.Index].Task : null;
        open.Execute(BoardCommands.MoveTask(open.Board, task, zones[target].Group, landsBefore));
        return true;
    }
}
