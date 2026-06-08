using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Arrow keys while composing a formula "poke" at cells (Excel point mode): each arrow inserts/moves
/// a cell reference at the caret instead of moving the text caret, and Shift extends it to a range.
/// </summary>
public class FormulaPointModeTests
{
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static MainWindow Editing(out IAppEnvironment env)
    {
        var window = new MainWindow();
        window.Show();
        env = window;
        env.Selection.MoveTo(0, 0); // A1
        env.BeginEdit();
        Pump();
        return window;
    }

    [AvaloniaFact]
    public void Arrows_poke_a_single_cell_reference()
    {
        var window = Editing(out var env);

        window.KeyTextInput("=");
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);  // A2
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);  // A3
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); // B3
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();

        Assert.Equal("B3", env.Session.ActiveSheet.Find(0, 0)!.Formula);
    }

    [AvaloniaFact]
    public void Shift_arrow_extends_the_poke_into_a_range()
    {
        var window = Editing(out var env);

        window.KeyTextInput("=SUM(");
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);  // A2
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Shift); // extend to A2:A3
        window.KeyTextInput(")");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();

        Assert.Equal("SUM(A2:A3)", env.Session.ActiveSheet.Find(0, 0)!.Formula);
    }
}
