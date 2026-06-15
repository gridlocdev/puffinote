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
/// The simple per-column filter dropdown is <b>single-column</b> (PRD §8.12): filtering one column
/// then another <em>replaces</em> the first rather than stacking, and a table stores its filter on its
/// own record (not the sheet AutoFilter). Multi-column sort/filter pipelines belong to the Sort &amp;
/// Filter dialog, so the dropdown stays unambiguous.
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
    public void Filtering_a_second_column_replaces_the_first()
    {
        var (window, sheet, range) = TwoColumnTable();

        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 0, "A"); // Team = A
        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 1, "X"); // then City = X replaces it

        // Only City=X constrains now: rows 1 (A,X) and 3 (B,X) show; row 2 (A,Y) hides.
        Assert.False(sheet.IsRowHidden(1));
        Assert.True(sheet.IsRowHidden(2));  // A,Y rejected by City=X
        Assert.False(sheet.IsRowHidden(3)); // B,X passes (Team filter no longer applies)
        Assert.Equal(new[] { 1 }, window.Grid.ActiveFilterColumnsForTest); // a single active filter column
    }

    [AvaloniaFact]
    public void The_table_owns_its_filter_not_the_sheet_autofilter()
    {
        var (window, sheet, range) = TwoColumnTable();
        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 0, "A");

        var table = Assert.Single(sheet.Tables);
        Assert.NotNull(table.Filter);
        Assert.Contains(0, table.Filter!.Columns.Keys);
        Assert.Null(sheet.ActiveFilterSpec); // never leaks to the sheet-level AutoFilter
        Assert.Null(sheet.ActiveFilter);
    }

    [AvaloniaFact]
    public void The_dropdown_no_longer_lists_other_columns_filters()
    {
        var (window, _, range) = TwoColumnTable();
        window.Grid.ApplyColumnFilterForTest(range, hasHeader: true, column: 0, "A");

        // Open column B's dropdown — it is purely about column B; no cross-column "Active filters" stack.
        var chevron = window.Grid.FilterButtonRectForTest(1);
        Assert.NotNull(chevron);
        ClickGridPoint(window, chevron!.Value.Center);
        window.CaptureRenderedFrame();

        Assert.False(window.Grid.PopupHasTextForTest("Active filters"));
        Assert.False(window.Grid.PopupHasTextForTest("Team")); // the other column isn't shown
    }
}
