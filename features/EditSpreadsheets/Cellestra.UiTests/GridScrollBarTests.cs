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
/// Overlay scroll bars (task 3): a bar appears in a direction only when content stretches past the
/// viewport there — including text that spills rightward past its cell — and dragging it scrolls.
/// </summary>
public class GridScrollBarTests
{
    private static (MainWindow Window, Worksheet Sheet, SpreadsheetGrid Grid) Open()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        Layout(window.Editor.Grid);
        return (window, window.Editor.Session.ActiveSheet, window.Editor.Grid);
    }

    private static void Layout(SpreadsheetGrid grid)
    {
        grid.InvalidateMeasure();
        grid.InvalidateArrange();
        grid.Measure(new Size(640, 400));
        grid.Arrange(new Rect(0, 0, 640, 400));
    }

    [AvaloniaFact]
    public void An_empty_sheet_shows_no_scroll_bars()
    {
        var (_, _, grid) = Open();
        Assert.False(grid.VerticalScrollBarVisibleForTest);
        Assert.False(grid.HorizontalScrollBarVisibleForTest);
    }

    [AvaloniaFact]
    public void Content_below_the_viewport_shows_the_vertical_bar()
    {
        var (_, sheet, grid) = Open();
        sheet.SetValue(200, 0, CellValue.Number(1)); // far below the ~18 visible rows
        Layout(grid);

        Assert.True(grid.VerticalScrollBarVisibleForTest);
        Assert.True(grid.VerticalScrollMaxForTest > 0);
        Assert.False(grid.HorizontalScrollBarVisibleForTest); // nothing wide → no horizontal bar
    }

    [AvaloniaFact]
    public void Content_right_of_the_viewport_shows_the_horizontal_bar()
    {
        var (_, sheet, grid) = Open();
        sheet.SetValue(0, 60, CellValue.Number(1)); // far right of the ~9 visible columns
        Layout(grid);

        Assert.True(grid.HorizontalScrollBarVisibleForTest);
        Assert.True(grid.HorizontalScrollMaxForTest > 0);
    }

    [AvaloniaFact]
    public void Text_that_spills_far_right_pulls_out_the_horizontal_bar()
    {
        // One long label in A1, every neighbour empty: the overflow extends the content rightward
        // past the viewport even though no other cell holds a value (task 3).
        var (_, sheet, grid) = Open();
        sheet.SetValue(0, 0, CellValue.Text(new string('W', 240)));
        Layout(grid);

        Assert.True(grid.HorizontalScrollBarVisibleForTest);
    }

    [AvaloniaFact]
    public void Dragging_the_vertical_bar_scrolls_the_grid()
    {
        var (_, sheet, grid) = Open();
        sheet.SetValue(300, 0, CellValue.Number(1));
        Layout(grid);

        Assert.Equal(0, grid.ScrollRowForTest);
        grid.DragVerticalScrollForTest(50);
        Assert.Equal(50, grid.ScrollRowForTest);
    }

    [AvaloniaFact]
    public void A_small_trackpad_delta_scrolls_a_sub_cell_distance_without_snapping_to_a_row()
    {
        // A trackpad delivers small fractional deltas; each must move a few pixels, not jump a whole row
        // (the old behaviour leapt three rows per event, which read as "extremely fast").
        var (_, sheet, grid) = Open();
        sheet.SetValue(300, 0, CellValue.Number(1));
        Layout(grid);

        grid.WheelScrollForTest(0, -0.25); // a gentle fractional nudge downward (toward later rows)

        Assert.Equal(0, grid.ScrollRowForTest);        // still anchored on row 0 …
        Assert.True(grid.ScrollRowOffsetForTest > 0);  // … but glided partway into it
        Assert.True(grid.ScrollRowOffsetForTest < 20); // less than a default row height — a sub-cell move
    }

    [AvaloniaFact]
    public void Accumulated_wheel_scrolling_advances_the_anchor_row_smoothly()
    {
        var (_, sheet, grid) = Open();
        sheet.SetValue(300, 0, CellValue.Number(1));
        Layout(grid);

        // Several notches add up past a row boundary: the anchor advances and a remainder stays as the
        // sub-cell offset, so scrolling is continuous rather than quantised to whole rows.
        for (int i = 0; i < 5; i++)
        {
            grid.WheelScrollForTest(0, -1);
        }

        Assert.True(grid.ScrollRowForTest >= 1);
        Assert.True(grid.ScrollRowOffsetForTest >= 0);
    }

    [AvaloniaFact]
    public void Scrolling_back_up_returns_to_the_very_top()
    {
        var (_, sheet, grid) = Open();
        sheet.SetValue(300, 0, CellValue.Number(1));
        Layout(grid);

        grid.WheelScrollForTest(0, -3); // down a bit
        Layout(grid);
        grid.WheelScrollForTest(0, 10); // and well back up

        Assert.Equal(0, grid.ScrollRowForTest);
        Assert.Equal(0, grid.ScrollRowOffsetForTest);
    }

    [AvaloniaFact]
    public void Shift_wheel_scrolls_horizontally()
    {
        var (_, sheet, grid) = Open();
        sheet.SetValue(0, 60, CellValue.Number(1));
        Layout(grid);

        grid.WheelScrollForTest(-2, 0); // a horizontal (e.g. Shift-mapped) delta

        Assert.True(grid.ScrollColumnForTest > 0 || grid.ScrollColumnOffsetForTest > 0);
        Assert.Equal(0, grid.ScrollRowForTest); // vertical axis untouched
    }
}
