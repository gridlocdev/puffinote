using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// While editing, the inline editor grows rightwards over the neighbouring cells as the text widens
/// (Google Sheets/Excel feel) — never narrower than its cell, never growing downward.
/// </summary>
public class InlineEditorGrowthTests
{
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void Layout(SpreadsheetGrid grid)
    {
        grid.InvalidateMeasure();
        grid.InvalidateArrange();
        grid.Measure(new Size(640, 400));
        grid.Arrange(new Rect(0, 0, 640, 400));
    }

    [AvaloniaFact]
    public void The_editor_grows_rightwards_as_the_text_widens()
    {
        var window = new MainWindow();
        window.Show();
        window.Grid.Zoom = 1.0;
        Layout(window.Grid);
        var env = (IAppEnvironment)window;
        env.Selection.MoveTo(0, 0); // A1
        env.BeginEdit();
        Pump();

        window.Grid.SetInlineEditorText("x");
        Layout(window.Grid);
        double narrow = window.Grid.EditorBoundsForTest.Width;

        window.Grid.SetInlineEditorText("this is a very long cell value that runs well past one column");
        Layout(window.Grid);
        double wide = window.Grid.EditorBoundsForTest.Width;

        Assert.True(wide > narrow, $"expected the editor to widen ({narrow} → {wide})");
        Assert.True(wide > Cellestra.Core.Defaults.ColumnWidth, "the editor should grow past a single column");
    }

    [AvaloniaFact]
    public void The_editor_never_shrinks_below_its_cell_or_grows_downward()
    {
        var window = new MainWindow();
        window.Show();
        window.Grid.Zoom = 1.0;
        Layout(window.Grid);
        var env = (IAppEnvironment)window;
        env.Selection.MoveTo(0, 0);
        env.BeginEdit();
        Pump();

        window.Grid.SetInlineEditorText("hi"); // short text
        Layout(window.Grid);
        var shortBounds = window.Grid.EditorBoundsForTest;
        Assert.True(shortBounds.Width >= Cellestra.Core.Defaults.ColumnWidth - 0.5); // never below the cell width

        window.Grid.SetInlineEditorText("a much longer value than the cell can hold on its own");
        Layout(window.Grid);
        var longBounds = window.Grid.EditorBoundsForTest;

        // Growing the text widens the editor but never makes it taller — it never grows downward.
        Assert.True(longBounds.Width > shortBounds.Width);
        Assert.Equal(shortBounds.Height, longBounds.Height, precision: 1);
    }
}
