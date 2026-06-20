using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Copy/paste through the real UI clipboard path keeps the formula and shifts its relative
/// references by the paste offset, while absolute ($) references stay put (Golden Rule #8).
/// </summary>
public class CopyPasteFormulaTests
{
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void Copy_then_paste_shifts_relative_refs_but_not_absolute()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Number(10));                 // A1
        sheet.SetFormula(0, 1, "A1*2+$A$1", CellValue.Number(30));  // B1 = A1*2+$A$1

        env.Selection.MoveTo(0, 1); // B1
        env.Copy();
        Pump();

        env.Selection.MoveTo(1, 1); // B2
        env.Paste();
        Pump();

        // Relative A1 -> A2 (one row down); absolute $A$1 stays.
        Assert.Equal("A2*2+$A$1", sheet.Find(1, 1)!.Formula);
    }
}
