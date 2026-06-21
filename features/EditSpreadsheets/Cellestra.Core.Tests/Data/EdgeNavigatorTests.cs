using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Data;

/// <summary>Excel-style ⌘+Arrow "jump to edge" navigation (PRD parity).</summary>
public class EdgeNavigatorTests
{
    private static Worksheet SheetWith(params (int Row, int Column, double Value)[] cells)
    {
        var sheet = new Worksheet("Sheet1");
        foreach (var (row, column, value) in cells)
        {
            sheet.SetValue(row, column, CellValue.Number(value));
        }

        return sheet;
    }

    [Fact]
    public void Within_a_contiguous_run_jumps_to_the_last_filled_cell()
    {
        var sheet = SheetWith((0, 0, 1), (1, 0, 2), (2, 0, 3), (3, 0, 4));

        Assert.Equal((3, 0), EdgeNavigator.FindEdge(sheet, 0, 0, rowDelta: 1, columnDelta: 0));
    }

    [Fact]
    public void Facing_a_gap_jumps_across_to_the_next_filled_cell()
    {
        // A1 filled, A2–A4 empty, A5 filled.
        var sheet = SheetWith((0, 0, 1), (4, 0, 5));

        Assert.Equal((4, 0), EdgeNavigator.FindEdge(sheet, 0, 0, rowDelta: 1, columnDelta: 0));
    }

    [Fact]
    public void From_an_empty_cell_jumps_to_the_next_filled_cell()
    {
        var sheet = SheetWith((3, 0, 1));

        Assert.Equal((3, 0), EdgeNavigator.FindEdge(sheet, 0, 0, rowDelta: 1, columnDelta: 0));
    }

    [Fact]
    public void From_the_last_filled_cell_jumps_to_the_sheet_edge()
    {
        var sheet = SheetWith((0, 0, 1), (1, 0, 2));

        Assert.Equal((SheetDimensions.MaxRows - 1, 0), EdgeNavigator.FindEdge(sheet, 1, 0, rowDelta: 1, columnDelta: 0));
    }

    [Fact]
    public void Empty_sheet_jumps_to_the_boundary()
    {
        var sheet = new Worksheet("Sheet1");

        Assert.Equal((0, SheetDimensions.MaxColumns - 1), EdgeNavigator.FindEdge(sheet, 0, 0, rowDelta: 0, columnDelta: 1));
        Assert.Equal((0, 0), EdgeNavigator.FindEdge(sheet, 0, 0, rowDelta: -1, columnDelta: 0)); // already at top edge
    }

    [Fact]
    public void Works_horizontally_to_the_right()
    {
        var sheet = SheetWith((0, 0, 1), (0, 1, 2), (0, 2, 3));

        Assert.Equal((0, 2), EdgeNavigator.FindEdge(sheet, 0, 0, rowDelta: 0, columnDelta: 1));
    }
}
