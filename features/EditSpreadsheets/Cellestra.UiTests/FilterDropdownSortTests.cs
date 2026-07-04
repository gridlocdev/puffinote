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
/// The table filter dropdown's "Sort A→Z" / "Z→A" must reorder the data rows (keeping the header in
/// place), driven through the real chevron → popup → button click path (#5).
/// </summary>
public class FilterDropdownSortTests
{
    private static MainWindow TableWindow()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;

        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Score")); // header
        sheet.SetValue(1, 0, CellValue.Number(30));
        sheet.SetValue(2, 0, CellValue.Number(10));
        sheet.SetValue(3, 0, CellValue.Number(20));
        sheet.AddTable(new Table("T", CellRange.Parse("A1:A4"), HasHeader: true));

        window.Editor.Grid.Refresh();
        window.CaptureRenderedFrame(); // populate the rendered filter-button rects
        return window;
    }

    private static void ClickGridPoint(MainWindow window, Point gridLocal)
    {
        var p = window.Editor.Grid.TranslatePoint(gridLocal, window) ?? default;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
    }

    [AvaloniaFact]
    public void Filter_dropdown_sort_ascending_reorders_data_and_keeps_header()
    {
        var window = TableWindow();
        var sheet = window.Editor.Session.ActiveSheet;

        // Open the dropdown by clicking column A's chevron.
        var chevron = window.Editor.Grid.FilterButtonRectForTest(0);
        Assert.NotNull(chevron);
        ClickGridPoint(window, chevron!.Value.Center);
        window.CaptureRenderedFrame(); // lay out the popup so its buttons have bounds

        // Click "Sort A→Z" in the popup.
        var sortCenter = window.Editor.Grid.PopupControlCenterForTest("Sort A→Z");
        Assert.NotNull(sortCenter);
        ClickGridPoint(window, sortCenter!.Value);

        Assert.Equal("Score", sheet.GetValue(0, 0).AsText); // header stays in row 1
        Assert.Equal(10, sheet.GetValue(1, 0).AsNumber);    // data sorted ascending below it
        Assert.Equal(20, sheet.GetValue(2, 0).AsNumber);
        Assert.Equal(30, sheet.GetValue(3, 0).AsNumber);
        Assert.NotNull(sheet.Tables[0].Sort); // sort persisted on the table for round-trip
    }

    [AvaloniaFact]
    public void Esc_closes_the_filter_dropdown_popup()
    {
        var window = TableWindow();
        window.Editor.Grid.Focus();

        var chevron = window.Editor.Grid.FilterButtonRectForTest(0);
        Assert.NotNull(chevron);
        ClickGridPoint(window, chevron!.Value.Center);
        window.CaptureRenderedFrame();
        Assert.True(window.Editor.Grid.PopupHasTextForTest("Sorting Score")); // the dropdown is open

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(window.Editor.Grid.PopupHasTextForTest("Sorting Score")); // Esc dismissed it
    }

    [AvaloniaFact]
    public void Filter_popup_is_titled_by_the_column_header()
    {
        var window = TableWindow(); // header "Score" in A1
        var chevron = window.Editor.Grid.FilterButtonRectForTest(0);
        ClickGridPoint(window, chevron!.Value.Center);
        window.CaptureRenderedFrame();

        Assert.True(window.Editor.Grid.PopupHasTextForTest("Sorting Score")); // #6: titled by the header
    }

    [AvaloniaFact]
    public void Filter_dropdown_sort_moves_whole_rows_by_the_clicked_column()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        var sheet = window.Editor.Session.ActiveSheet;
        // Two-column table; sort by column B should carry column A's cells along with each row.
        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Score"));
        sheet.SetValue(1, 0, CellValue.Text("Cara"));
        sheet.SetValue(1, 1, CellValue.Number(30));
        sheet.SetValue(2, 0, CellValue.Text("Ana"));
        sheet.SetValue(2, 1, CellValue.Number(10));
        sheet.AddTable(new Table("T", CellRange.Parse("A1:B3"), HasHeader: true));
        window.Editor.Grid.Refresh();
        window.CaptureRenderedFrame();

        var chevron = window.Editor.Grid.FilterButtonRectForTest(1); // column B's dropdown
        Assert.NotNull(chevron);
        ClickGridPoint(window, chevron!.Value.Center);
        window.CaptureRenderedFrame();
        ClickGridPoint(window, window.Editor.Grid.PopupControlCenterForTest("Sort A→Z")!.Value);

        Assert.Equal(10, sheet.GetValue(1, 1).AsNumber);  // sorted by B ascending
        Assert.Equal("Ana", sheet.GetValue(1, 0).AsText); // column A moved with its row
    }

    [AvaloniaFact]
    public void Sorting_a_second_column_replaces_the_first_single_level_in_the_dropdown()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Team"));
        sheet.SetValue(0, 1, CellValue.Text("Score"));
        sheet.SetValue(1, 0, CellValue.Text("A"));
        sheet.SetValue(1, 1, CellValue.Number(30));
        sheet.SetValue(2, 0, CellValue.Text("B"));
        sheet.SetValue(2, 1, CellValue.Number(10));
        sheet.AddTable(new Table("T", CellRange.Parse("A1:B3"), HasHeader: true));
        window.Editor.Grid.Refresh();
        window.CaptureRenderedFrame();

        // Sort by column A, then by column B — the simple dropdown is single-column, so B replaces A.
        ClickGridPoint(window, window.Editor.Grid.FilterButtonRectForTest(0)!.Value.Center);
        window.CaptureRenderedFrame();
        ClickGridPoint(window, window.Editor.Grid.PopupControlCenterForTest("Sort A→Z")!.Value);

        ClickGridPoint(window, window.Editor.Grid.FilterButtonRectForTest(1)!.Value.Center);
        window.CaptureRenderedFrame();
        ClickGridPoint(window, window.Editor.Grid.PopupControlCenterForTest("Sort A→Z")!.Value);

        // The table owns its sort; only column B remains (multi-column "then by" is the dialog's job).
        var table = Assert.Single(sheet.Tables);
        Assert.NotNull(table.Sort);
        Assert.Equal([1], table.Sort!.Levels.Select(l => l.Column));
        Assert.Null(sheet.ActiveSort); // a table never leaks its sort to the sheet AutoFilter
    }

    [AvaloniaFact]
    public void Filter_dropdown_sort_descending_reorders_data()
    {
        var window = TableWindow();
        var sheet = window.Editor.Session.ActiveSheet;

        var chevron = window.Editor.Grid.FilterButtonRectForTest(0);
        Assert.NotNull(chevron);
        ClickGridPoint(window, chevron!.Value.Center);
        window.CaptureRenderedFrame();

        var sortCenter = window.Editor.Grid.PopupControlCenterForTest("Sort Z→A");
        Assert.NotNull(sortCenter);
        ClickGridPoint(window, sortCenter!.Value);

        Assert.Equal("Score", sheet.GetValue(0, 0).AsText);
        Assert.Equal(30, sheet.GetValue(1, 0).AsNumber); // descending
        Assert.Equal(10, sheet.GetValue(3, 0).AsNumber);
    }
}
