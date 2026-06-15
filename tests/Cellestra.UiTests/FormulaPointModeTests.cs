using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
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
    public void Point_mode_reference_mirrors_to_the_formula_bar()
    {
        // A grid self-edit (point-mode poke) must still raise InlineEditTextChanged so the formula bar
        // keeps tracking the cell — only the bar's own echo is suppressed.
        var window = Editing(out _);

        string? last = null;
        window.Grid.InlineEditTextChanged += t => last = t;

        window.KeyTextInput("=");
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // poke A2 into the formula
        Pump();

        Assert.Equal("=A2", last);
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

    [AvaloniaFact]
    public void Clicking_a_cell_while_typing_a_formula_inserts_its_reference()
    {
        var window = new MainWindow();
        window.Show();
        window.Grid.Zoom = 1.0;
        window.CaptureRenderedFrame();
        var env = (IAppEnvironment)window;
        env.Selection.MoveTo(0, 0); // A1
        env.BeginEdit();
        Pump();

        window.KeyTextInput("=");
        // Click C2 (column 2, row 1): its centre in grid space.
        var c2 = window.Grid.TranslatePoint(CellCentre(2, 1), window) ?? default;
        window.MouseDown(c2, MouseButton.Left);
        window.MouseUp(c2, MouseButton.Left);
        Pump();
        Assert.True(window.Grid.IsInlineEditing, "the click should insert a reference, not commit the edit");
        Assert.Equal("=C2", window.Grid.EditorText);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();

        Assert.Equal("C2", env.Session.ActiveSheet.Find(0, 0)!.Formula);
    }

    [AvaloniaFact]
    public void Dragging_a_range_while_typing_a_formula_inserts_the_range()
    {
        var window = new MainWindow();
        window.Show();
        window.Grid.Zoom = 1.0;
        window.CaptureRenderedFrame();
        var env = (IAppEnvironment)window;
        env.Selection.MoveTo(0, 0); // A1
        env.BeginEdit();
        Pump();

        window.KeyTextInput("=SUM(");
        var b2 = window.Grid.TranslatePoint(CellCentre(1, 1), window) ?? default; // B2
        var d4 = window.Grid.TranslatePoint(CellCentre(3, 3), window) ?? default; // D4
        window.MouseDown(b2, MouseButton.Left);
        window.MouseMove(d4);
        window.MouseUp(d4, MouseButton.Left);
        window.KeyTextInput(")");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();

        Assert.Equal("SUM(B2:D4)", env.Session.ActiveSheet.Find(0, 0)!.Formula);
    }

    [AvaloniaFact]
    public void Clicking_a_cell_while_typing_in_the_formula_bar_inserts_a_reference()
    {
        var window = new MainWindow();
        window.Show();
        window.Grid.Zoom = 1.0;
        window.CaptureRenderedFrame();
        var env = (IAppEnvironment)window;
        env.Selection.MoveTo(0, 0); // A1

        window.FormulaInput.Focus();
        window.FormulaInput.Text = "="; // typing in the bar mirrors into the inline editor
        Pump();
        Assert.True(window.Grid.IsInlineEditing);

        var c2 = window.Grid.TranslatePoint(CellCentre(2, 1), window) ?? default;
        window.MouseDown(c2, MouseButton.Left);
        window.MouseUp(c2, MouseButton.Left);
        Pump();

        // The click inserted the reference into the formula instead of committing the bar's edit.
        Assert.True(window.Grid.IsInlineEditing);
        Assert.Equal("=C2", window.FormulaInput.Text);
    }

    /// <summary>Centre of a cell (zoom 1, default sizes), in the grid's own coordinate space.</summary>
    private static Point CellCentre(int column, int row)
    {
        const double w = Cellestra.Core.Defaults.ColumnWidth;
        const double h = Cellestra.Core.Defaults.RowHeight;
        return new Point(
            SpreadsheetGrid.HeaderWidth + column * w + w / 2,
            SpreadsheetGrid.HeaderHeight + row * h + h / 2);
    }
}
