using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

/// <summary>
/// Which hidden lines an Unhide reaches (PRD §8.14). The interesting cases are all about the fact that
/// a hidden line can't be selected, so the selection the user <em>can</em> make sits beside the lines
/// they want back.
/// </summary>
public class HiddenLinesTests
{
    private static Worksheet New() => Workbook.CreateNew().ActiveSheet;

    private static void HideRows(Worksheet sheet, params int[] rows)
    {
        foreach (int row in rows)
        {
            sheet.GetOrCreateRow(row).Hidden = true;
        }
    }

    [Fact]
    public void Nothing_hidden_means_nothing_to_unhide()
    {
        var sheet = New();
        Assert.Empty(HiddenLines.RowsToUnhide(sheet, 0, 10));
        Assert.Empty(HiddenLines.ColumnsToUnhide(sheet, 0, 10));
    }

    [Fact]
    public void Hidden_rows_inside_the_selection_come_back_as_one_run()
    {
        var sheet = New();
        HideRows(sheet, 4, 5, 6);

        // Rows 3..7 selected (the run plus a visible row either side).
        Assert.Equal([(4, 6)], HiddenLines.RowsToUnhide(sheet, 3, 7));
    }

    [Fact]
    public void The_row_above_a_hidden_run_reaches_the_whole_run()
    {
        var sheet = New();
        HideRows(sheet, 4, 5, 6);

        // Right-clicking row 3 alone — the only header left touching the run from above.
        Assert.Equal([(4, 6)], HiddenLines.RowsToUnhide(sheet, 3, 3));

        // …and from below (row 7).
        Assert.Equal([(4, 6)], HiddenLines.RowsToUnhide(sheet, 7, 7));
    }

    [Fact]
    public void The_reach_stops_at_the_first_visible_line()
    {
        var sheet = New();
        HideRows(sheet, 4, 5);
        HideRows(sheet, 8, 9); // a second run, with visible rows 6..7 between

        // From row 3 only the adjacent run is in reach — the far run is behind visible rows.
        Assert.Equal([(4, 5)], HiddenLines.RowsToUnhide(sheet, 3, 3));

        // A selection spanning both runs restores both, each as its own run.
        Assert.Equal([(4, 5), (8, 9)], HiddenLines.RowsToUnhide(sheet, 3, 10));
    }

    [Fact]
    public void A_selection_nowhere_near_a_hidden_run_finds_nothing()
    {
        var sheet = New();
        HideRows(sheet, 4, 5, 6);

        Assert.Empty(HiddenLines.RowsToUnhide(sheet, 20, 25));
    }

    [Fact]
    public void A_whole_sheet_selection_reports_only_the_hidden_runs()
    {
        // The point of returning runs rather than one span: unhiding after ⌘A must cost what is
        // hidden, not the million rows the selection covers.
        var sheet = New();
        HideRows(sheet, 4, 5, 6);

        var runs = HiddenLines.RowsToUnhide(sheet, 0, SheetDimensions.MaxRows - 1);
        Assert.Equal([(4, 6)], runs);
    }

    [Fact]
    public void Columns_behave_the_same_as_rows()
    {
        var sheet = New();
        sheet.GetOrCreateColumn(2).Hidden = true;
        sheet.GetOrCreateColumn(3).Hidden = true;

        Assert.Equal([(2, 3)], HiddenLines.ColumnsToUnhide(sheet, 1, 1));
        Assert.Equal([(2, 3)], HiddenLines.ColumnsToUnhide(sheet, 4, 4));
        Assert.Empty(HiddenLines.ColumnsToUnhide(sheet, 8, 9));
    }

    [Fact]
    public void The_reach_never_runs_off_the_edge_of_the_sheet()
    {
        var sheet = New();
        HideRows(sheet, 0, 1);

        Assert.Equal([(0, 1)], HiddenLines.RowsToUnhide(sheet, 2, 2));
        Assert.Equal([(0, 1)], HiddenLines.RowsToUnhide(sheet, 0, 0));
    }
}
