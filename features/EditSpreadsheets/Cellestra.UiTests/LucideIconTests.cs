using Avalonia;
using Avalonia.Headless.XUnit;
using BirdBrain.Components.Controls.Icons;
using Xunit;

namespace Cellestra.UiTests;

public class LucideIconTests
{
    [AvaloniaTheory]
    [InlineData("bold")]
    [InlineData("italic")]
    [InlineData("align-left")]
    [InlineData("filter")]
    [InlineData("sort-asc")]
    [InlineData("dollar-sign")]
    [InlineData("calendar")]
    [InlineData("chevron-down")]
    [InlineData("table-cells-merge")] // exercises <rect rx ry>
    [InlineData("scissors")]          // exercises <circle>
    [InlineData("paintbrush")]        // Format Painter glyph (was paint-bucket)
    [InlineData("grid-2x2")]          // All Borders glyph
    [InlineData("square")]            // Outer Border glyph
    [InlineData("square-x")]          // No Border glyph
    public void Vendored_glyph_parses_to_non_empty_geometry(string key)
    {
        var geometry = LucideIcons.Get(key);
        Assert.NotNull(geometry);
        Assert.True(geometry!.Bounds.Width > 0 && geometry.Bounds.Height > 0);
    }

    [AvaloniaFact]
    public void Unknown_key_resolves_to_null_without_throwing()
    {
        Assert.Null(LucideIcons.Get("definitely-not-a-real-icon"));
        Assert.Null(LucideIcons.Get(null));
        Assert.Null(LucideIcons.Get(string.Empty));
    }

    [AvaloniaFact]
    public void Icon_control_measures_to_its_size()
    {
        var icon = new LucideIcon { IconKey = "bold", Size = 18 };
        icon.Measure(Size.Infinity);
        Assert.Equal(new Size(18, 18), icon.DesiredSize);
    }
}
