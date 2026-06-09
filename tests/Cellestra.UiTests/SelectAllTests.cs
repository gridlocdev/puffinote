using System.Linq;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class SelectAllTests
{
    private static void RunSelectAll(IAppEnvironment env) =>
        AppCommands.All.First(c => c.Id == "edit.selectAll").Execute(env);

    [AvaloniaFact]
    public void First_select_all_picks_the_region_then_the_whole_sheet()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        var sheet = env.Session.ActiveSheet;

        // A 2x2 data block at A1:B2, with the active cell inside it.
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetValue(0, 1, CellValue.Number(2));
        sheet.SetValue(1, 0, CellValue.Number(3));
        sheet.SetValue(1, 1, CellValue.Number(4));
        env.Selection.MoveTo(0, 0);

        RunSelectAll(env);
        Assert.Equal(0, env.Selection.Top);
        Assert.Equal(0, env.Selection.Left);
        Assert.Equal(1, env.Selection.Bottom);
        Assert.Equal(1, env.Selection.Right); // the current region

        RunSelectAll(env);
        Assert.Equal(0, env.Selection.Top);
        Assert.Equal(SheetDimensions.MaxRows - 1, env.Selection.Bottom);
        Assert.Equal(SheetDimensions.MaxColumns - 1, env.Selection.Right); // the whole sheet
    }

    // ---- SelectAllExpander (pure expansion logic, no window) -----------------

    private static Worksheet TwoByTwoSheet()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetValue(0, 1, CellValue.Number(2));
        sheet.SetValue(1, 0, CellValue.Number(3));
        sheet.SetValue(1, 1, CellValue.Number(4));
        return sheet;
    }

    [Fact]
    public void Expander_first_press_returns_the_data_region()
    {
        var sheet = TwoByTwoSheet();
        var current = new CellRange(new CellAddress(0, 0)); // single active cell A1

        var next = SelectAllExpander.Next(sheet, current, activeRow: 0, activeColumn: 0);

        Assert.Equal(0, next.Top);
        Assert.Equal(0, next.Left);
        Assert.Equal(1, next.Bottom);
        Assert.Equal(1, next.Right);
    }

    [Fact]
    public void Expander_second_press_widens_to_the_whole_sheet()
    {
        var sheet = TwoByTwoSheet();
        var region = new CellRange(new CellAddress(0, 0), new CellAddress(1, 1)); // already at the region

        var next = SelectAllExpander.Next(sheet, region, activeRow: 0, activeColumn: 0);

        Assert.Equal(0, next.Top);
        Assert.Equal(0, next.Left);
        Assert.Equal(SheetDimensions.MaxRows - 1, next.Bottom);
        Assert.Equal(SheetDimensions.MaxColumns - 1, next.Right);
    }

    [Fact]
    public void Expander_on_an_isolated_empty_cell_goes_straight_to_the_whole_sheet()
    {
        // An empty cell's "current region" is just itself; the single-cell selection already
        // covers it, so ⌘A widens to the whole sheet immediately (matching Excel on a blank cell).
        var sheet = TwoByTwoSheet();
        var current = new CellRange(new CellAddress(9, 9)); // empty area, away from the data

        var next = SelectAllExpander.Next(sheet, current, activeRow: 9, activeColumn: 9);

        Assert.Equal(0, next.Top);
        Assert.Equal(0, next.Left);
        Assert.Equal(SheetDimensions.MaxRows - 1, next.Bottom);
        Assert.Equal(SheetDimensions.MaxColumns - 1, next.Right);
    }
}
