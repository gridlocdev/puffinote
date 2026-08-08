using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
        window.Editor.Grid.Zoom = 1.0;
        Layout(window.Editor.Grid);
        var env = (IAppEnvironment)window.Editor;
        env.Selection.MoveTo(0, 0); // A1
        env.BeginEdit();
        Pump();

        window.Editor.Grid.SetInlineEditorText("x");
        Layout(window.Editor.Grid);
        double narrow = window.Editor.Grid.EditorBoundsForTest.Width;

        window.Editor.Grid.SetInlineEditorText("this is a very long cell value that runs well past one column");
        Layout(window.Editor.Grid);
        double wide = window.Editor.Grid.EditorBoundsForTest.Width;

        Assert.True(wide > narrow, $"expected the editor to widen ({narrow} → {wide})");
        Assert.True(wide > Cellestra.Core.Defaults.ColumnWidth, "the editor should grow past a single column");
    }

    /// <summary>
    /// Before it grows anywhere, the editor sits exactly on its cell. Avalonia centres a control it
    /// could not shrink to the rect it was handed, so the theme's stock TextBox minimum (64×32) used to
    /// lift the editor 6px above a default 20px row — the text you were typing sat above the text you
    /// were about to commit.
    /// </summary>
    [AvaloniaFact]
    public void The_editor_sits_exactly_on_the_cell_it_edits()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        Layout(window.Editor.Grid);
        var env = (IAppEnvironment)window.Editor;
        env.Selection.MoveTo(4, 2); // C5 — a plain cell at default width and height
        env.BeginEdit();
        Pump();

        window.Editor.Grid.SetInlineEditorText("hi"); // too short to grow the editor anywhere
        Layout(window.Editor.Grid);

        Assert.Equal(window.Editor.Grid.ActiveEditorRect, window.Editor.Grid.EditorBoundsForTest);
    }

    [AvaloniaFact]
    public void The_editor_never_shrinks_below_its_cell_or_grows_downward()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        Layout(window.Editor.Grid);
        var env = (IAppEnvironment)window.Editor;
        env.Selection.MoveTo(0, 0);
        env.BeginEdit();
        Pump();

        window.Editor.Grid.SetInlineEditorText("hi"); // short text
        Layout(window.Editor.Grid);
        var shortBounds = window.Editor.Grid.EditorBoundsForTest;
        Assert.True(shortBounds.Width >= Cellestra.Core.Defaults.ColumnWidth - 0.5); // never below the cell width

        window.Editor.Grid.SetInlineEditorText("a much longer value than the cell can hold on its own");
        Layout(window.Editor.Grid);
        var longBounds = window.Editor.Grid.EditorBoundsForTest;

        // Growing the text widens the editor but never makes it taller — it never grows downward.
        Assert.True(longBounds.Width > shortBounds.Width);
        Assert.Equal(shortBounds.Height, longBounds.Height, precision: 1);
    }
}
