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
