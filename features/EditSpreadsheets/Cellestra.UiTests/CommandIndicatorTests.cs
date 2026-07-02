using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Command-bar / catalog indicators: Merge Cells lights when the active cell is inside a merge, and
/// Increase/Decrease Decimal disable on cells whose number-format kind carries no decimal places.
/// </summary>
public class CommandIndicatorTests
{
    [AvaloniaFact]
    public void Merge_command_is_active_when_active_cell_is_in_a_merge()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        var merge = env.Commands.ById("format.merge")!;

        env.Selection.MoveTo(0, 0);
        Assert.False(merge.IsToggleActive);

        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3"))); // rows 1..2, cols 1..2
        env.Selection.MoveTo(1, 1);
        Assert.True(merge.IsToggleActive);

        env.Selection.MoveTo(0, 0); // outside the merge again
        Assert.False(merge.IsToggleActive);
    }

    [AvaloniaFact]
    public void Decimal_steppers_disable_on_date_and_text_cells()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        var inc = env.Commands.ById("format.increaseDecimal")!;
        var dec = env.Commands.ById("format.decreaseDecimal")!;

        // A plain number cell — steppers are enabled.
        sheet.SetValue(0, 0, CellValue.Number(1.5));
        env.Selection.MoveTo(0, 0);
        Assert.True(inc.IsEnabled);
        Assert.True(dec.IsEnabled);

        // A date-formatted number — no fraction digits to step, so they disable.
        sheet.SetValue(1, 0, CellValue.Number(45292));
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, sheet.Name, CellRange.Parse("A2"),
            s => s.WithNumberFormat(new NumberFormat("yyyy-mm-dd")), "Date"));
        env.Selection.MoveTo(1, 0);
        Assert.False(inc.IsEnabled);
        Assert.False(dec.IsEnabled);

        // A text cell — not numeric context at all.
        sheet.SetValue(2, 0, CellValue.Text("hello"));
        env.Selection.MoveTo(2, 0);
        Assert.False(inc.IsEnabled);
        Assert.False(dec.IsEnabled);
    }
}
