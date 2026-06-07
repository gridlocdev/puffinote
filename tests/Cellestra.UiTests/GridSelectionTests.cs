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
