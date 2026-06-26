using System.Collections.Generic;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.RadialMenu;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>Pure geometry/config for the radial menu — no window needed (AGENTS.md: test the logic headlessly).</summary>
public class RadialMenuLayoutTests
{
    [Theory]
    [InlineData(0, -50, 0)] // straight up   → segment 0
    [InlineData(50, 0, 1)]  // right         → segment 1
    [InlineData(0, 50, 2)]  // straight down → segment 2
    [InlineData(-50, 0, 3)] // left          → segment 3
    public void Segment_is_picked_by_direction_clockwise_from_top(double dx, double dy, int expected) =>
        Assert.Equal(expected, RadialMenuLayout.SegmentAt(dx, dy, segmentCount: 4, deadZoneRadius: 40));

    [Fact]
    public void Inside_the_dead_zone_selects_nothing() =>
        Assert.Equal(-1, RadialMenuLayout.SegmentAt(5, 5, segmentCount: 4, deadZoneRadius: 40));

    [Fact]
    public void No_segments_selects_nothing() =>
        Assert.Equal(-1, RadialMenuLayout.SegmentAt(0, -50, segmentCount: 0, deadZoneRadius: 40));

    [Fact]
    public void Default_config_has_six_seeded_segments()
    {
        var config = RadialMenuConfig.Default;
        Assert.Equal(RadialMenuConfig.DefaultSegmentCount, config.SegmentCount);
        Assert.Equal(config.SegmentCount, config.CommandIds.Count);
        Assert.Equal("edit.cut", config.CommandIdAt(0));
    }

    [Theory]
    [InlineData(99, RadialMenuConfig.MaxSegments)]
    [InlineData(0, RadialMenuConfig.MinSegments)]
    public void Normalized_clamps_the_segment_count(int requested, int expected)
    {
        var config = new RadialMenuConfig { SegmentCount = requested }.Normalized();
        Assert.Equal(expected, config.SegmentCount);
        Assert.Equal(expected, config.CommandIds.Count);
    }

    [Fact]
    public void Normalized_pads_new_slots_from_the_defaults()
    {
        var config = new RadialMenuConfig { SegmentCount = 8, CommandIds = new() }.Normalized();
        Assert.Equal(8, config.CommandIds.Count);
        Assert.Equal(RadialMenuConfig.DefaultCommandIds[7], config.CommandIdAt(7));
    }

    [Fact]
    public void Normalized_preserves_an_explicit_unbound_slot()
    {
        var config = new RadialMenuConfig
        {
            SegmentCount = 3,
            CommandIds = new List<string?> { "edit.copy", null, "edit.paste" },
        }.Normalized();

        Assert.Equal("edit.copy", config.CommandIdAt(0));
        Assert.Null(config.CommandIdAt(1)); // the deliberate gap survives, not re-seeded from defaults
        Assert.Equal("edit.paste", config.CommandIdAt(2));
    }
}
