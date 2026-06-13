using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Text too wide for its cell box is clipped (not spilled) and flagged with a red double-chevron at
/// the clipped edge, à la LibreOffice Calc. Right-aligned text is clipped on the left, everything
/// else on the right; text that fits — or spills into empty neighbours — gets no marker.
/// </summary>
public class OverflowMarkerTests
{
    private static (MainWindow Window, Worksheet Sheet, SpreadsheetGrid Grid) Open()
    {
        var window = new MainWindow();
        window.Show();
        window.Grid.Measure(new Size(800, 600));
        window.Grid.Arrange(new Rect(0, 0, 800, 600));
        window.CaptureRenderedFrame(); // build the visible column/row layout
        return (window, window.Session.ActiveSheet, window.Grid);
    }

    [AvaloniaFact]
    public void The_chevron_rule_flags_only_text_wider_than_its_box()
    {
        // Fits → no marker.
        Assert.Null(SpreadsheetGrid.OverflowChevronSide(textWidth: 40, availableWidth: 60, isRightAligned: false));
        // Equal within tolerance → no marker.
        Assert.Null(SpreadsheetGrid.OverflowChevronSide(60, 60, false));
        // Wider, left-aligned → chevron on the right.
        Assert.Equal(true, SpreadsheetGrid.OverflowChevronSide(120, 60, isRightAligned: false));
        // Wider, right-aligned → chevron on the left.
        Assert.Equal(false, SpreadsheetGrid.OverflowChevronSide(120, 60, isRightAligned: true));
    }

    [AvaloniaFact]
    public void Long_text_clipped_by_an_occupied_neighbour_gets_a_right_chevron()
    {
        var (window, sheet, grid) = Open();
        sheet.SetValue(0, 0, CellValue.Text("Wednesday the seventeenth of September"));
        sheet.SetValue(0, 1, CellValue.Text("blocks the spill")); // B1 occupied → no room to overflow
        window.CaptureRenderedFrame();

        Assert.Equal(true, grid.OverflowMarkerSideForTest(0, 0));
    }

    [AvaloniaFact]
    public void Text_that_can_spill_into_empty_neighbours_gets_no_marker()
    {
        var (window, sheet, grid) = Open();
        sheet.SetValue(0, 0, CellValue.Text("Wed")); // short — fits its own column
        window.CaptureRenderedFrame();

        Assert.Null(grid.OverflowMarkerSideForTest(0, 0));
    }

    [AvaloniaFact]
    public void Clipped_render_does_not_throw()
    {
        var (window, sheet, _) = Open();
        sheet.SetValue(0, 0, CellValue.Text("A very long string that cannot fit inside one default column"));
        sheet.SetValue(0, 1, CellValue.Text("x"));
        var frame = window.CaptureRenderedFrame(); // real Skia render path through the marker code
        Assert.NotNull(frame);
    }
}
