using Puffinote.Components.Theming;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The family's one zoom ladder. Every app steps along it, so its edges are where the bugs would be shared
/// too: stepping past an end, stepping from a value that isn't on the ladder, and a factor restored from a
/// settings file someone hand-edited.
/// </summary>
public class ZoomScaleTests
{
    [Fact]
    public void The_ladder_runs_from_50_to_400_percent_and_only_ever_ascends()
    {
        Assert.Equal(ZoomScale.Min, ZoomScale.Stops[0]);
        Assert.Equal(ZoomScale.Max, ZoomScale.Stops[^1]);
        Assert.Contains(ZoomScale.Default, ZoomScale.Stops);

        for (int i = 1; i < ZoomScale.Stops.Count; i++)
        {
            Assert.True(ZoomScale.Stops[i] > ZoomScale.Stops[i - 1], $"stop {i} does not ascend");
        }
    }

    [Fact]
    public void Zooming_in_then_out_returns_to_where_it_started()
    {
        foreach (double stop in ZoomScale.Stops)
        {
            if (stop >= ZoomScale.Max)
            {
                continue;
            }

            Assert.Equal(stop, ZoomScale.Out(ZoomScale.In(stop)));
        }
    }

    [Fact]
    public void The_ends_hold_rather_than_wrapping_or_overshooting()
    {
        Assert.Equal(ZoomScale.Max, ZoomScale.In(ZoomScale.Max));
        Assert.Equal(ZoomScale.Min, ZoomScale.Out(ZoomScale.Min));

        // Past the end is still the end — a stored 12.0 must not become 48.0 on the next ⌘=.
        Assert.Equal(ZoomScale.Max, ZoomScale.In(12.0));
        Assert.Equal(ZoomScale.Min, ZoomScale.Out(0.01));
    }

    [Fact]
    public void An_off_ladder_value_steps_to_a_neighbour_never_the_wrong_way()
    {
        // 1.3 is not a stop (an older build's ±0.1 could leave one behind, and so can a hand-edited file).
        // Zooming in from it must go up and zooming out must go down — the failure here would be silently
        // snapping to the nearest stop first, which reads as a zoom-in that shrank the page.
        Assert.True(ZoomScale.In(1.3) > 1.3);
        Assert.True(ZoomScale.Out(1.3) < 1.3);
        Assert.Equal(1.5, ZoomScale.In(1.3));
        Assert.Equal(1.25, ZoomScale.Out(1.3));
    }

    [Fact]
    public void Stepping_off_a_stop_does_not_stall_on_it()
    {
        // The tolerance that lets a JSON round-trip still count as "on" 1.25 must not swallow the step.
        Assert.Equal(1.5, ZoomScale.In(1.25));
        Assert.Equal(1.1, ZoomScale.Out(1.25));
    }

    [Fact]
    public void A_corrupt_or_absent_factor_falls_back_to_100_percent()
    {
        Assert.Equal(ZoomScale.Default, ZoomScale.Clamp(double.NaN));
        Assert.Equal(ZoomScale.Default, ZoomScale.Clamp(double.PositiveInfinity));
        Assert.Equal(ZoomScale.Min, ZoomScale.Clamp(0));
        Assert.Equal(ZoomScale.Max, ZoomScale.Clamp(99));
    }

    [Fact]
    public void The_readout_is_a_whole_percentage()
    {
        Assert.Equal("100%", ZoomScale.Format(1.0));
        Assert.Equal("125%", ZoomScale.Format(1.25));
        Assert.Equal("67%", ZoomScale.Format(0.67));
        Assert.Equal("400%", ZoomScale.Format(4.0));
    }
}
