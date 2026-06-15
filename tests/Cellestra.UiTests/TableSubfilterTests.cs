using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Table subfiltering (task 4): filtering one column then another AND-combines (a row survives only
/// when every active filter accepts it), and each column's dropdown lists the active filters with
/// ascending/descending sort and a delete button to drop one.
/// </summary>
public class TableSubfilterTests
{
    private static (MainWindow Window, Worksheet Sheet, CellRange Range) TwoColumnTable()
    {
        var window = new MainWindow();
        window.Show();
        window.Grid.Zoom = 1.0;
        var sheet = window.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Team")); sheet.SetValue(0, 1, CellValue.Text("City"));
        sheet.SetValue(1, 0, CellValue.Text("A")); sheet.SetValue(1, 1, CellValue.Text("X"));
        sheet.SetValue(2, 0, CellValue.Text("A")); sheet.SetValue(2, 1, CellValue.Text("Y"));
        sheet.SetValue(3, 0, CellValue.Text("B")); sheet.SetValue(3, 1, CellValue.Text("X"));
        var range = CellRange.Parse("A1:B4");
        sheet.AddTable(new Table("T", range, HasHeader: true));
        window.Grid.Refresh();
        window.CaptureRenderedFrame();
        return (window, sheet, range);
    }

    private static void ClickGridPoint(MainWindow window, Point gridLocal)
    {
        var p = window.Grid.TranslatePoint(gridLocal, window) ?? default;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
    }

    [AvaloniaFact]
    public void Filtering_two_columns_and_combines_them()
    {
        var (window, sheet, range) = TwoColumnTable();

        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 0, "A"); // Team = A
        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 1, "X"); // then City = X

        // Only row 1 (A,X) survives both filters; the other two data rows hide.
        Assert.False(sheet.IsRowHidden(1));
        Assert.True(sheet.IsRowHidden(2));  // A,Y rejected by City=X
        Assert.True(sheet.IsRowHidden(3));  // B,X rejected by Team=A
        Assert.Equal(new[] { 0, 1 }, window.Grid.ActiveFilterColumnsForTest);
    }

    [AvaloniaFact]
    public void The_dropdown_lists_the_active_filters_for_every_column()
    {
        var (window, _, range) = TwoColumnTable();
        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 0, "A");
        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 1, "X");

        // Open column A's dropdown — it still lists BOTH filtered fields.
        var chevron = window.Grid.FilterButtonRectForTest(0);
        Assert.NotNull(chevron);
        ClickGridPoint(window, chevron!.Value.Center);
        window.CaptureRenderedFrame();

        Assert.True(window.Grid.PopupHasTextForTest("Active filters"));
        Assert.True(window.Grid.PopupHasTextForTest("Team"));
        Assert.True(window.Grid.PopupHasTextForTest("City"));
        Assert.Equal(2, window.Grid.PopupButtonCountForTest("✕")); // a delete button per active filter
    }

    [AvaloniaFact]
    public void Deleting_an_active_filter_from_the_list_drops_only_that_column()
    {
        var (window, sheet, range) = TwoColumnTable();
        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 0, "A");
        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 1, "X");

        var chevron = window.Grid.FilterButtonRectForTest(1); // open from column B's dropdown
        ClickGridPoint(window, chevron!.Value.Center);
        window.CaptureRenderedFrame();

        // The first delete button is for the lowest-indexed active filter (column 0 / Team).
        var firstDelete = window.Grid.PopupControlCenterForTest("✕");
        Assert.NotNull(firstDelete);
        ClickGridPoint(window, firstDelete!.Value);

        Assert.Equal(new[] { 1 }, window.Grid.ActiveFilterColumnsForTest); // only City remains
        Assert.False(sheet.IsRowHidden(3)); // B,X now visible again (Team filter gone)
    }
}
