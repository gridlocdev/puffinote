using System.Linq;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
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
}
