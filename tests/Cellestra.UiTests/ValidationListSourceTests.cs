using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// A List validation rule can draw its allowed values from a cell range instead of a literal list
/// (PRD §8.13). The grid dropdown then reflects the live contents of that range, de-duplicated and
/// in document order, and a cross-sheet source resolves against the right worksheet.
/// </summary>
public class ValidationListSourceTests
{
    [AvaloniaFact]
    public void Dropdown_reflects_the_live_values_of_the_source_range()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Session.ActiveSheet;

        // E1:E4 holds the allowed values, with a duplicate and a blank to prove de-dup/skip.
        sheet.SetValue(0, 4, CellValue.Text("Red"));
        sheet.SetValue(1, 4, CellValue.Text("Green"));
        sheet.SetValue(2, 4, CellValue.Text("Red")); // duplicate
        // E4 left blank
        sheet.AddValidation(new DataValidation(ValidationKind.List, CellRange.Parse("A1:A3"))
        {
            ListSource = CellRange.Parse("E1:E4"),
        });

        var items = window.Grid.ValidationDropdownItemsForTest(0, 0);

        Assert.Equal(new[] { "Red", "Green" }, items);
    }

    [AvaloniaFact]
    public void Source_range_on_another_sheet_resolves_against_that_sheet()
    {
        var window = new MainWindow();
        window.Show();
        var workbook = window.Session.Workbook;
        var lists = workbook.AddSheet("Lists");
        lists.SetValue(0, 0, CellValue.Text("Yes"));
        lists.SetValue(1, 0, CellValue.Text("No"));

        var sheet = window.Session.ActiveSheet;
        sheet.AddValidation(new DataValidation(ValidationKind.List, CellRange.Parse("A1:A3"))
        {
            ListSource = CellRange.Parse("Lists.A1:A2"),
        });

        var items = window.Grid.ValidationDropdownItemsForTest(0, 0);

        Assert.Equal(new[] { "Yes", "No" }, items);
    }
}
