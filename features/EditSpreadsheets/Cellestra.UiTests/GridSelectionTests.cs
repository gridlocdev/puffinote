using Cellestra.Core.Model;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Xunit;

namespace Cellestra.UiTests;

public class GridSelectionTests
{
    [Fact]
    public void Cmd_click_adds_disjoint_ranges_and_a_plain_move_resets()
    {
        var selection = new GridSelection();
        selection.MoveTo(0, 0);
        Assert.False(selection.IsDiscontiguous);
        Assert.Single(selection.Ranges);

        selection.AddPoint(2, 2);
        Assert.True(selection.IsDiscontiguous);
        Assert.Equal(2, selection.Ranges.Count);
        Assert.Equal(2, selection.ActiveRow);    // the active cell moves to the new range
        Assert.Equal(2, selection.ActiveColumn);

        selection.MoveTo(5, 5);                   // a plain click collapses back to one range
        Assert.False(selection.IsDiscontiguous);
        Assert.Single(selection.Ranges);
    }

    [Fact]
    public void Extending_pins_the_active_cell_at_the_anchor()
    {
        // Click-and-drag (and Shift+extension): the bold active outline stays on the first cell
        // while the rectangle's moving corner tracks the cursor — like every other spreadsheet.
        var selection = new GridSelection();
        selection.MoveTo(2, 1);
        selection.ExtendTo(5, 4);

        Assert.Equal(2, selection.ActiveRow);
        Assert.Equal(1, selection.ActiveColumn);
        Assert.Equal(5, selection.ExtentRow);
        Assert.Equal(4, selection.ExtentColumn);
        Assert.Equal((2, 1, 5, 4), (selection.Top, selection.Left, selection.Bottom, selection.Right));
    }

    [Fact]
    public void Extending_up_and_left_past_the_anchor_still_pins_the_active_cell()
    {
        // Dragging the other way: the anchor becomes the rectangle's bottom-right, and the active
        // cell stays on it.
        var selection = new GridSelection();
        selection.MoveTo(3, 3);
        selection.ExtendTo(0, 0);

        Assert.Equal(3, selection.ActiveRow);
        Assert.Equal(3, selection.ActiveColumn);
        Assert.Equal((0, 0, 3, 3), (selection.Top, selection.Left, selection.Bottom, selection.Right));
    }

    [Fact]
    public void CollapseToOrigin_returns_to_the_anchored_start_cell()
    {
        var selection = new GridSelection();
        selection.MoveTo(2, 1);        // start at C2-ish anchor
        selection.ExtendTo(5, 4);      // drag/shift out to a block
        Assert.False(selection.IsSingleCell);

        selection.CollapseToOrigin();

        Assert.True(selection.IsSingleCell);
        Assert.Equal(2, selection.ActiveRow);   // back at the origin, not the active corner
        Assert.Equal(1, selection.ActiveColumn);
    }

    [Fact]
    public void CollapseToOrigin_drops_disjoint_cmd_click_ranges()
    {
        var selection = new GridSelection();
        selection.MoveTo(0, 0);
        selection.AddPoint(3, 3);      // a second, disjoint range
        Assert.True(selection.IsDiscontiguous);

        selection.CollapseToOrigin();

        Assert.False(selection.IsDiscontiguous);
        Assert.Single(selection.Ranges);
        Assert.Equal(3, selection.ActiveRow); // collapses to the most recent range's anchor
        Assert.Equal(3, selection.ActiveColumn);
    }

    [Fact]
    public void Add_range_appends_a_whole_column_without_dropping_the_first()
    {
        var selection = new GridSelection();
        selection.MoveTo(0, 0);

        selection.AddRange(0, 3, SheetDimensions.MaxRows - 1, 3);

        Assert.Equal(2, selection.Ranges.Count);
        var added = selection.Ranges[^1];
        Assert.Equal(3, added.Left);
        Assert.Equal(3, added.Right);
        Assert.Equal(0, selection.Ranges[0].Left); // the original A1 range is preserved
    }
}
