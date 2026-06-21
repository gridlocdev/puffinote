using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class GridResizeTests
{
    [AvaloniaFact]
    public void Dragging_a_column_header_border_resizes_the_column()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;          // pin zoom so border coordinates are deterministic
        window.CaptureRenderedFrame();   // rebuild the visible columns at this zoom

        var sheet = window.Editor.Session.ActiveSheet;
        double before = sheet.GetColumnWidth(0);

        // Column 0's right border sits at HeaderWidth + its (zoom-1) width, inside the header strip.
        var border = new Point(SpreadsheetGrid.HeaderWidth + before, SpreadsheetGrid.HeaderHeight / 2);
        var start = window.Editor.Grid.TranslatePoint(border, window) ?? default;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(40, 0));
        window.MouseUp(start + new Vector(40, 0), MouseButton.Left);

        Assert.True(sheet.GetColumnWidth(0) > before + 20, $"expected the column to widen (was {before}, now {sheet.GetColumnWidth(0)})");
    }

    [AvaloniaFact]
    public void Dragging_a_row_header_border_resizes_the_row()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        window.CaptureRenderedFrame();

        var sheet = window.Editor.Session.ActiveSheet;
        double before = sheet.GetRowHeight(0);

        // Row 0's bottom border sits at HeaderHeight + its height, inside the row-header strip.
        var border = new Point(SpreadsheetGrid.HeaderWidth / 2, SpreadsheetGrid.HeaderHeight + before);
        var start = window.Editor.Grid.TranslatePoint(border, window) ?? default;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, 24));
        window.MouseUp(start + new Vector(0, 24), MouseButton.Left);

        Assert.True(sheet.GetRowHeight(0) > before + 8, $"expected the row to grow (was {before}, now {sheet.GetRowHeight(0)})");
    }
}
