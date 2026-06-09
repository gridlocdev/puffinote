using Avalonia;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Selecting and navigating merged cells: the whole merge becomes the active selection, the Name Box
/// reports the full range, the inline editor spans the combined rectangle, and arrow keys skip over
/// the merge instead of landing inside it.
/// </summary>
public class MergedCellGridTests
{
    private static (Worksheet Sheet, SpreadsheetGrid Grid) Open()
    {
        var window = new MainWindow();
        window.Show();
        return (window.Session.ActiveSheet, window.Grid);
    }

    [AvaloniaFact]
    public void Selecting_a_covered_cell_selects_the_whole_region_and_names_the_range()
    {
        var (sheet, grid) = Open();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3"))); // rows 1..2, cols 1..2

        grid.SelectForTest(2, 2); // a covered cell (C3)

        Assert.Equal(1, grid.Selection.Top);
        Assert.Equal(1, grid.Selection.Left);
        Assert.Equal(2, grid.Selection.Bottom);
        Assert.Equal(2, grid.Selection.Right);
        Assert.Equal(1, grid.Selection.ActiveRow);   // active snaps to the anchor (B2)
        Assert.Equal(1, grid.Selection.ActiveColumn);
        Assert.Equal("B2:C3", grid.ActiveCellName);
    }

    [AvaloniaFact]
    public void Name_box_shows_single_address_for_an_unmerged_cell()
    {
        var (_, grid) = Open();
        grid.Selection.MoveTo(0, 0);
        Assert.Equal("A1", grid.ActiveCellName);
    }

    [AvaloniaFact]
    public void Arrow_into_a_merge_selects_it_and_arrow_out_skips_past_it()
    {
        var (sheet, grid) = Open();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3"))); // rows 1..2, cols 1..2

        // Start at A2 (row 1, col 0) and arrow right: lands on the merge, active = anchor.
        grid.Selection.MoveTo(1, 0);
        grid.NavigateForTest(0, 1, extend: false);
        Assert.Equal(1, grid.Selection.ActiveRow);
        Assert.Equal(1, grid.Selection.ActiveColumn);
        Assert.Equal("B2:C3", grid.ActiveCellName);

        // Arrow right again: skips the whole merge to D2 (col 3), not C2.
        grid.NavigateForTest(0, 1, extend: false);
        Assert.Equal(1, grid.Selection.ActiveRow);
        Assert.Equal(3, grid.Selection.ActiveColumn);

        // From the merge, arrow down skips past the bottom edge to row 3 (B4).
        grid.SelectForTest(1, 1);
        grid.NavigateForTest(1, 0, extend: false);
        Assert.Equal(3, grid.Selection.ActiveRow);
        Assert.Equal(1, grid.Selection.ActiveColumn);
    }

    [AvaloniaFact]
    public void Editing_a_merged_cell_sizes_the_editor_to_the_combined_rectangle()
    {
        var (sheet, grid) = Open();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:D2"))); // a 3-column-wide merge on one row
        sheet.SetValue(1, 1, CellValue.Text("merged"));

        grid.SelectForTest(1, 1);
        grid.BeginEdit(null);
        grid.Measure(new Size(800, 600));
        grid.Arrange(new Rect(0, 0, 800, 600));

        var rect = grid.ActiveEditorRect;
        double oneColumn = sheet.GetColumnWidth(1) * grid.Zoom;
        // The merged editor spans all three columns, so it's wider than ~2.5 single cells.
        Assert.True(rect.Width > oneColumn * 2.5, $"editor width {rect.Width} should span ~3 columns ({oneColumn} each)");
    }
}
