using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The touchpad swipe recognizer behind the sidebar's show/hide gesture: it has to fire once per physical
/// swipe, never on an ordinary scroll, and never on delta that drifted in from an earlier gesture.
/// </summary>
public sealed class SidebarSwipeTests
{
    /// <summary>A trackpad delivers a swipe as many small events; feed one at 10ms intervals.</summary>
    private static List<SwipeAction> Swipe(SidebarSwipe swipe, double totalX, int events = 6, ulong startMs = 1000)
    {
        var seen = new List<SwipeAction>();
        for (int i = 0; i < events; i++)
        {
            seen.Add(swipe.Feed(totalX / events, 0, startMs + ((ulong)i * 10)));
        }

        return seen;
    }

    [Fact]
    public void A_swipe_left_fires_once_and_the_rest_of_the_gesture_is_absorbed()
    {
        var swipe = new SidebarSwipe();
        var seen = Swipe(swipe, -SidebarSwipe.Threshold * 2, events: 8);

        Assert.Single(seen, a => a == SwipeAction.SwipeLeft);
        Assert.DoesNotContain(SwipeAction.SwipeRight, seen);

        // Everything after the one firing is swallowed, so the momentum tail can't collapse it straight back.
        int fired = seen.IndexOf(SwipeAction.SwipeLeft);
        Assert.All(seen.Skip(fired + 1), a => Assert.Equal(SwipeAction.Absorb, a));
    }

    [Fact]
    public void A_swipe_right_fires_the_opposite_direction()
    {
        var swipe = new SidebarSwipe();
        Assert.Single(Swipe(swipe, SidebarSwipe.Threshold * 2), a => a == SwipeAction.SwipeRight);
    }

    [Fact]
    public void Travel_short_of_the_threshold_is_never_a_swipe()
    {
        var swipe = new SidebarSwipe();
        Assert.All(Swipe(swipe, SidebarSwipe.Threshold * 0.9), a => Assert.Equal(SwipeAction.None, a));
    }

    [Fact]
    public void A_single_stray_notch_of_horizontal_scroll_does_nothing()
    {
        var swipe = new SidebarSwipe();
        Assert.Equal(SwipeAction.None, swipe.Feed(1.0, 0, 1000));
    }

    /// <summary>A vertical scroll with a bit of sideways wobble is a scroll, however long it goes on.</summary>
    [Fact]
    public void A_mostly_vertical_scroll_never_accumulates_into_a_swipe()
    {
        var swipe = new SidebarSwipe();
        for (int i = 0; i < 40; i++)
        {
            Assert.Equal(SwipeAction.None, swipe.Feed(-1.0, -3.0, 1000 + ((ulong)i * 10)));
        }
    }

    [Fact]
    public void An_idle_gap_ends_the_gesture_so_the_next_swipe_fires_again()
    {
        var swipe = new SidebarSwipe();
        Assert.Single(Swipe(swipe, -SidebarSwipe.Threshold * 2, startMs: 1000), a => a == SwipeAction.SwipeLeft);

        ulong later = 1000 + SidebarSwipe.IdleResetMs + 500;
        Assert.Single(Swipe(swipe, SidebarSwipe.Threshold * 2, startMs: later), a => a == SwipeAction.SwipeRight);
    }

    /// <summary>
    /// Two half-swipes minutes apart must not add up. Without the idle reset, leftover travel from an
    /// abandoned gesture would make some later, much smaller flick fire.
    /// </summary>
    [Fact]
    public void Travel_does_not_carry_across_an_idle_gap()
    {
        var swipe = new SidebarSwipe();
        Assert.All(Swipe(swipe, -SidebarSwipe.Threshold * 0.8, startMs: 1000), a => Assert.Equal(SwipeAction.None, a));

        // A lone small flick a minute later: on its own it is nowhere near the threshold, so nothing fires.
        Assert.Equal(SwipeAction.None, swipe.Feed(-SidebarSwipe.Threshold * 0.8, 0, 61_000));
    }

    [Fact]
    public void Reversing_direction_mid_gesture_starts_the_count_again()
    {
        var swipe = new SidebarSwipe();
        Assert.Equal(SwipeAction.None, swipe.Feed(-SidebarSwipe.Threshold * 0.9, 0, 1000));

        // The same magnitude the other way doesn't cancel to zero and it doesn't fire either — it restarts.
        Assert.Equal(SwipeAction.None, swipe.Feed(SidebarSwipe.Threshold * 0.9, 0, 1010));
        Assert.Equal(SwipeAction.SwipeRight, swipe.Feed(SidebarSwipe.Threshold * 0.9, 0, 1020));
    }

    /// <summary>
    /// Swipe one way, then straight back with no pause: the second swipe must fire. The trackpad's momentum
    /// tail keeps events arriving, so there is never an idle gap to end the first gesture — when the "already
    /// fired" latch cleared on idle alone, the entire return swipe was absorbed and the sidebar sat still
    /// until the user did something else long enough for the tail to die.
    /// </summary>
    [Fact]
    public void Swiping_straight_back_fires_without_waiting_for_an_idle_gap()
    {
        var swipe = new SidebarSwipe();
        Assert.Single(Swipe(swipe, -SidebarSwipe.Threshold * 2, startMs: 1000), a => a == SwipeAction.SwipeLeft);

        // The tail of that first swipe, still coasting left, 10ms apart — no gap anywhere in here.
        for (ulong t = 1060; t < 1200; t += 10)
        {
            Assert.Equal(SwipeAction.Absorb, swipe.Feed(-1.0, 0, t));
        }

        // Fingers go back down and swipe right while the tail is still warm.
        Assert.Single(Swipe(swipe, SidebarSwipe.Threshold * 2, startMs: 1210), a => a == SwipeAction.SwipeRight);
    }

    /// <summary>
    /// The flip side of the rule above: a tail is not perfectly monotone, and a few counter-signed jitter
    /// events in the middle of one must not add up into a phantom swipe back.
    /// </summary>
    [Fact]
    public void Jitter_inside_a_momentum_tail_is_not_a_swipe_back()
    {
        var swipe = new SidebarSwipe();
        Assert.Single(Swipe(swipe, -SidebarSwipe.Threshold * 2, startMs: 1000), a => a == SwipeAction.SwipeLeft);

        ulong t = 1060;
        for (int i = 0; i < 30; i++, t += 10)
        {
            // A decaying leftward tail speckled with small rightward wobble.
            Assert.NotEqual(SwipeAction.SwipeRight, swipe.Feed(i % 4 == 3 ? 0.4 : -2.0, 0, t));
        }
    }

    /// <summary>A clock that jumps backwards (or a fresh input session) is treated as a new gesture, not a 500-million-year gap.</summary>
    [Fact]
    public void A_backwards_timestamp_restarts_rather_than_underflowing()
    {
        var swipe = new SidebarSwipe();
        Assert.Equal(SwipeAction.None, swipe.Feed(-SidebarSwipe.Threshold * 0.9, 0, 5000));
        Assert.Equal(SwipeAction.None, swipe.Feed(-SidebarSwipe.Threshold * 0.9, 0, 10));
        Assert.Equal(SwipeAction.SwipeLeft, swipe.Feed(-SidebarSwipe.Threshold * 0.9, 0, 20));
    }
}
