using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// There are two text boxes on screen — the one floating over the cell and the one in the formula bar —
/// and they mirror each other's text. ⇧↩ puts a line break in that shared text, but it may only make the
/// box the caret is in taller: a break typed into the cell must not push the whole toolbar row down.
/// </summary>
public class FormulaBarLineBreakTests
{
    private static MainWindow Open()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        window.Editor.Grid.Measure(new Size(640, 400));
        window.Editor.Grid.Arrange(new Rect(0, 0, 640, 400));
        return window;
    }

    private static double MeasuredHeight(Avalonia.Controls.Control control)
    {
        control.InvalidateMeasure();
        control.Measure(new Size(400, double.PositiveInfinity));
        return control.DesiredSize.Height;
    }

    [AvaloniaFact]
    public void A_line_break_typed_in_the_cell_leaves_the_formula_bar_one_line_tall()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var grid = window.Editor.Grid;
        var bar = window.Editor.FormulaInput;

        env.Selection.MoveTo(0, 0);
        grid.BeginEdit("one");
        Dispatcher.UIThread.RunJobs();
        double before = MeasuredHeight(bar);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("one\n", grid.EditorText);  // the cell took the break...
        Assert.Equal("one\n", bar.Text);         // ...and the bar still mirrors it
        Assert.Equal(before, MeasuredHeight(bar)); // ...without growing
    }

    [AvaloniaFact]
    public void Shift_return_in_the_formula_bar_breaks_the_line_there_instead_of_committing()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var bar = window.Editor.FormulaInput;

        env.Selection.MoveTo(0, 0);
        bar.Focus();
        bar.Text = "one";
        bar.CaretIndex = 3;
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("one\n", bar.Text);
        Assert.Equal(4, bar.CaretIndex);
        Assert.True(window.Editor.Grid.IsInlineEditing);        // ⇧↩ does not commit...
        Assert.Equal("one\n", window.Editor.Grid.EditorText);   // ...and the cell mirrors the break
    }

    [AvaloniaFact]
    public void Breaking_a_line_in_the_formula_bar_grows_the_formula_bar()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var bar = window.Editor.FormulaInput;

        env.Selection.MoveTo(0, 0);
        bar.Focus();
        bar.Text = "one";
        bar.CaretIndex = 3;
        Dispatcher.UIThread.RunJobs();
        double before = MeasuredHeight(bar);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        // The caret is in the bar, so the bar is the box that grows — otherwise the line you just made
        // would be invisible behind the one-line clamp.
        Assert.True(MeasuredHeight(bar) > before);
    }

    [AvaloniaFact]
    public void Plain_return_in_the_formula_bar_still_commits()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var bar = window.Editor.FormulaInput;

        env.Selection.MoveTo(0, 0);
        bar.Focus();
        bar.Text = "one";
        bar.CaretIndex = 3;
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.Editor.Grid.IsInlineEditing);
        Assert.Equal("one", env.Session.ActiveSheet.GetValue(0, 0).AsText);
    }
}
