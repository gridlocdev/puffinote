using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The inline editor belongs to a cell, so it may only be on screen while that cell is. Typing into
/// the formula bar opens a mirrored (focus-less) edit on the active cell, and that cell can easily be
/// out of view — a tall wrapped row above it is enough. The editor used to be "hidden" by arranging it
/// at a zero-size rect, which does not hide anything: the theme floors a TextBox at 64×32 and centres
/// it in the rect, so it reappeared as a stray box over the grid's top-left corner.
/// </summary>
public class InlineEditorOffscreenTests
{
    private static readonly Size Viewport = new(640, 400);

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void Layout(SpreadsheetGrid grid)
    {
        grid.InvalidateMeasure();
        grid.InvalidateArrange();
        grid.Measure(Viewport);
        grid.Arrange(new Rect(Viewport));
    }

    /// <summary>Open a window on B3, then scroll far enough down that B3 is no longer drawn.</summary>
    private static MainWindow OpenWithActiveCellScrolledAway()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        Layout(window.Editor.Grid);

        ((IAppEnvironment)window.Editor).Selection.MoveTo(2, 1); // B3
        Layout(window.Editor.Grid);

        window.Editor.Grid.WheelScrollForTest(0, -40);
        Layout(window.Editor.Grid);
        Assert.True(window.Editor.Grid.ScrollRowForTest > 2, "the active cell should have scrolled out of view");
        return window;
    }

    private static void TypeInFormulaBar(MainWindow window, string text)
    {
        window.Editor.FormulaInput.Focus();
        window.Editor.FormulaInput.Text = text;
        Pump();
        Layout(window.Editor.Grid);
    }

    [AvaloniaFact]
    public void An_edit_mirrored_from_the_formula_bar_draws_nothing_when_its_cell_is_off_screen()
    {
        var window = OpenWithActiveCellScrolledAway();

        TypeInFormulaBar(window, "aaaaaaaaaaaaaaa");

        Assert.True(window.Editor.Grid.IsInlineEditing); // the edit is live — it just has nowhere to draw
        Assert.False(
            new Rect(Viewport).Intersects(window.Editor.Grid.EditorBoundsForTest),
            $"the inline editor should be parked off the grid, was at {window.Editor.Grid.EditorBoundsForTest}");
    }

    [AvaloniaFact]
    public void Scrolling_the_cell_back_into_view_puts_the_editor_back_on_it()
    {
        var window = OpenWithActiveCellScrolledAway();
        var grid = window.Editor.Grid;
        TypeInFormulaBar(window, "back");

        grid.WheelScrollForTest(0, 40); // return to the top, where B3 lives
        Layout(grid);

        Assert.Equal(0, grid.ScrollRowForTest);
        Assert.Equal(grid.ActiveEditorRect.Position, grid.EditorBoundsForTest.Position); // back on its cell
        Assert.True(new Rect(Viewport).Contains(grid.EditorBoundsForTest.Center));
    }

    [AvaloniaFact]
    public void Scrolling_an_open_edit_out_of_view_does_not_commit_it()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        Layout(window.Editor.Grid);
        var env = (IAppEnvironment)window.Editor;
        env.Selection.MoveTo(2, 1); // B3
        env.BeginEdit();
        Pump();
        window.Editor.Grid.SetInlineEditorText("uncommitted");
        Layout(window.Editor.Grid);

        window.Editor.Grid.WheelScrollForTest(0, -40);
        Layout(window.Editor.Grid);

        Assert.True(window.Editor.Grid.IsInlineEditing);
        Assert.Equal("uncommitted", window.Editor.Grid.EditorText);
        Assert.Null(env.Session.ActiveSheet.Find(2, 1)); // nothing written to B3
    }
}
