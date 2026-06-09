using Cellestra.Core;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Pure tests for the row-height growth rule: a row grows to fit its tallest font so increasing a
/// cell's font size doesn't clip/overlap the next row, while the default-sized row stays at 20px.
/// </summary>
public class RowHeightCalculatorTests
{
    private const double VerticalPadding = 2;

    [Fact]
    public void Default_font_does_not_grow_the_default_row()
    {
        // The 10pt default must fit inside the 20px default row (no growth).
        double effective = RowHeightCalculator.Effective(Defaults.RowHeight, Defaults.FontSizePoints, VerticalPadding);
        Assert.Equal(Defaults.RowHeight, effective);
    }

    [Fact]
    public void A_larger_font_grows_the_row()
    {
        double small = RowHeightCalculator.Effective(Defaults.RowHeight, 10, VerticalPadding);
        double large = RowHeightCalculator.Effective(Defaults.RowHeight, 36, VerticalPadding);
        Assert.True(large > small, $"36pt row ({large}) should be taller than 10pt row ({small})");
        // The grown row is at least as tall as the glyph box plus padding.
        Assert.True(large >= RowHeightCalculator.ForFontSize(36, VerticalPadding));
    }

    [Fact]
    public void Row_never_shrinks_below_an_explicit_height()
    {
        // A hand-resized tall row keeps its height even with a small font.
        double effective = RowHeightCalculator.Effective(rowHeight: 80, tallestFontPoints: 10, VerticalPadding);
        Assert.Equal(80, effective);
    }

    [Fact]
    public void No_cells_in_a_row_leaves_its_height_untouched()
    {
        double effective = RowHeightCalculator.Effective(Defaults.RowHeight, tallestFontPoints: 0, VerticalPadding);
        Assert.Equal(Defaults.RowHeight, effective);
    }

    [Fact]
    public void Height_grows_monotonically_with_font_size()
    {
        double prev = 0;
        foreach (int pt in new[] { 8, 12, 18, 24, 48, 72 })
        {
            double h = RowHeightCalculator.ForFontSize(pt, VerticalPadding);
            Assert.True(h > prev, $"{pt}pt height {h} should exceed previous {prev}");
            prev = h;
        }
    }
}
