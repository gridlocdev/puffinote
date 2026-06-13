using Cellestra.Core.Model;
using Xunit;

namespace Cellestra.Core.Tests.Model;

public class CellRangeTests
{
    [Fact]
    public void Parse_single_cell_is_one_by_one()
    {
        var range = CellRange.Parse("C5");

        Assert.True(range.IsSingleCell);
        Assert.Equal(1, range.CellCount);
        Assert.Equal(2, range.Left);
        Assert.Equal(4, range.Top);
    }

    [Fact]
    public void Parse_normalizes_corner_order()
    {
        var range = CellRange.Parse("C5:A1");

        Assert.Equal(0, range.Left);
        Assert.Equal(0, range.Top);
        Assert.Equal(2, range.Right);
        Assert.Equal(4, range.Bottom);
        Assert.Equal(3, range.ColumnCount);
        Assert.Equal(5, range.RowCount);
        Assert.Equal(15, range.CellCount);
    }

    [Fact]
    public void Sheet_qualifier_on_start_applies_to_range()
    {
        var range = CellRange.Parse("Sheet1.A1:B2");

        Assert.Equal("Sheet1", range.Sheet);
        Assert.Equal("Sheet1.A1:B2", range.ToString());
    }

    [Theory]
    [InlineData("Sheet1.A1:Sheet2.B2")] // conflicting sheet on the end corner
    [InlineData("A1:Sheet2.B2")]         // lone sheet qualifier on the end corner
    public void Cross_sheet_range_is_rejected(string text)
    {
        Assert.False(CellRange.TryParse(text, out _));
    }

    [Fact]
    public void Matching_sheet_on_both_corners_parses()
    {
        Assert.True(CellRange.TryParse("Sheet1.A1:Sheet1.B2", out var range));
        Assert.Equal("Sheet1", range.Sheet);
    }

    [Fact]
    public void Contains_and_Intersects()
    {
        var a = CellRange.Parse("B2:D4");

        Assert.True(a.Contains(2, 2));
        Assert.False(a.Contains(0, 0));
        Assert.True(a.Intersects(CellRange.Parse("D4:F6")));
        Assert.False(a.Intersects(CellRange.Parse("E5:F6")));
    }

    [Fact]
    public void Positions_enumerates_row_major()
    {
        var range = CellRange.Parse("A1:B2");

        Assert.Equal(
            new[] { (0, 0), (0, 1), (1, 0), (1, 1) },
            range.Positions().ToArray());
    }
}
