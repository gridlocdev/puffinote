using Puffinote.Components.Input;
using Puffinote.Components.Theming;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The accumulator behind pinch-to-zoom. A trackpad hands it dozens of fractional deltas per gesture, so
/// what is worth pinning is everything that happens <i>between</i> those deltas: what gathers, what carries,
/// and what has to be thrown away so one pinch can't finish the one before it.
/// </summary>
public class PinchZoomGestureTests
{
    // Comfortably inside PinchZoomGesture.IdleResetMs, so a run of these is one continuous gesture.
    private const ulong Tick = 20;

    [Fact]
    public void Deltas_too_small_to_matter_gather_into_one_stop_rather_than_none()
    {
        var pinch = new PinchZoomGesture();

        Assert.Equal(0, pinch.Feed(0.3, 100));
        Assert.Equal(0, pinch.Feed(0.3, 100 + Tick));
        Assert.Equal(0, pinch.Feed(0.3, 100 + (2 * Tick)));

        // 1.2 stops' worth of travel: one stop now, the remaining 0.2 kept for the rest of the gesture.
        Assert.Equal(1, pinch.Feed(0.3, 100 + (3 * Tick)));
        Assert.Equal(0, pinch.Feed(0.3, 100 + (4 * Tick)));
        Assert.Equal(1, pinch.Feed(0.5, 100 + (5 * Tick)));
    }

    [Fact]
    public void One_event_can_be_worth_several_stops_at_once()
    {
        var pinch = new PinchZoomGesture();
        Assert.Equal(3, pinch.Feed(3.4, 100));
        Assert.Equal(-2, pinch.Feed(-2.5, 100 + Tick));
    }

    [Fact]
    public void A_gap_between_gestures_drops_the_leftover_travel()
    {
        var pinch = new PinchZoomGesture();
        Assert.Equal(0, pinch.Feed(0.9, 100));

        // Fingers left the trackpad. The 0.9 that never became a stop must not be waiting here to make the
        // next pinch answer early — a nudge minutes later starts from nothing.
        Assert.Equal(0, pinch.Feed(0.9, 100 + PinchZoomGesture.IdleResetMs));
        Assert.Equal(1, pinch.Feed(0.2, 100 + PinchZoomGesture.IdleResetMs + Tick));
    }

    [Fact]
    public void A_clock_that_jumps_backwards_is_treated_as_a_fresh_gesture_not_a_500_million_year_one()
    {
        var pinch = new PinchZoomGesture();
        Assert.Equal(0, pinch.Feed(0.9, 5_000));
        Assert.Equal(0, pinch.Feed(0.9, 100)); // reset, not carried
    }

    [Fact]
    public void Pinching_back_counts_from_zero_so_it_answers_after_one_stop()
    {
        var pinch = new PinchZoomGesture();
        Assert.Equal(0, pinch.Feed(0.9, 100));

        // A reversal is a new intent. Netting it against the 0.9 would leave -0.1 and swallow the whole
        // gesture back; starting over means one stop's travel out is one stop out.
        Assert.Equal(-1, pinch.Feed(-1.0, 100 + Tick));
    }

    [Fact]
    public void A_delta_with_no_direction_neither_steps_nor_disturbs_what_is_gathering()
    {
        var pinch = new PinchZoomGesture();
        Assert.Equal(0, pinch.Feed(0.9, 100));

        // The still moment at the top of a pinch, and a driver hiccup. Both say nothing about direction, so
        // neither may count as a reversal and wipe the travel that has genuinely accumulated.
        Assert.Equal(0, pinch.Feed(0, 100 + Tick));
        Assert.Equal(0, pinch.Feed(double.NaN, 100 + (2 * Tick)));
        Assert.Equal(0, pinch.Feed(double.PositiveInfinity, 100 + (3 * Tick)));

        Assert.Equal(1, pinch.Feed(0.2, 100 + (4 * Tick)));
    }

    [Fact]
    public void No_single_event_is_worth_more_than_the_whole_ladder()
    {
        var pinch = new PinchZoomGesture();

        // The caller walks the ladder a stop at a time; a synthetic delta must not turn that into a loop of
        // a million iterations to reach a 400% it hit on the thirteenth.
        Assert.Equal(ZoomScale.Stops.Count, pinch.Feed(1e9, 100));
        Assert.Equal(-ZoomScale.Stops.Count, pinch.Feed(-1e9, 100 + Tick));

        // ...and the absurd remainder is dropped rather than left to fire again on the next flick.
        Assert.Equal(0, pinch.Feed(0.5, 100 + (2 * Tick)));
    }
}
