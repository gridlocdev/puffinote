using Cellestra.Core.Model;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Dragging the fill handle extends the fill in BOTH axes at once — the target is the bounding box
/// of the source and the cursor, not a single dominant row/column (so a diagonal drag fills a block).
/// </summary>
public class FillTargetTests
{
    private static CellRange Cell(int row, int column) => new(new CellAddress(column, row), new CellAddress(column, row));

    [Fact]
    public void Diagonal_drag_from_a_single_cell_fills_the_whole_box()
    {
        // Source A1, drag down-and-right to C5 → fill A1:C5 (3 columns × 5 rows), not just one axis.
        var target = SpreadsheetGrid.ComputeFillTarget(Cell(0, 0), row: 4, column: 2);

        Assert.Equal(0, target.Top);
        Assert.Equal(0, target.Left);
        Assert.Equal(4, target.Bottom);
        Assert.Equal(2, target.Right);
    }

    [Fact]
    public void Drag_keeps_the_source_inside_the_box_and_extends_up_left_too()
    {
        var source = new CellRange(new CellAddress(2, 2), new CellAddress(3, 3)); // C3:D4

        // Cursor above-and-left of the source grows the box outward in both directions.
        var target = SpreadsheetGrid.ComputeFillTarget(source, row: 0, column: 0);

        Assert.Equal(0, target.Top);
        Assert.Equal(0, target.Left);
        Assert.Equal(3, target.Bottom); // still includes the source's far corner
        Assert.Equal(3, target.Right);
    }
}
