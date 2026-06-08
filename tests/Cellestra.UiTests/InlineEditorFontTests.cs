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
/// The inline cell editor renders its text the same size as the cell's drawn text and top-aligned,
/// so starting an edit doesn't make the text jump in size or position.
/// </summary>
public class InlineEditorFontTests
{
    private const double PointsToPixels = 96.0 / 72.0;

    [AvaloniaFact]
    public void Editor_font_matches_the_cell_style_and_is_top_aligned()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        var sheet = env.Session.ActiveSheet;
        var grid = window.Grid;

        // A cell at the default font size.
        sheet.SetValue(0, 0, CellValue.Text("test"));
        env.Selection.MoveTo(0, 0);
        grid.BeginEdit(null);
        Assert.True(grid.EditorTextIsTopAligned);
        // The editor matches the cell's drawn size: points → pixels, scaled by the current zoom.
        Assert.Equal(Cellestra.Core.Defaults.FontSizePoints * PointsToPixels * grid.Zoom, grid.EditorFontSize, 3);

        // A cell with a larger explicit font size: the editor follows it.
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, sheet.Name, CellRange.Parse("B2"),
            s => s with { Font = s.Font with { SizePoints = 20 } }));
        env.Selection.MoveTo(1, 1);
        grid.BeginEdit(null);
        Assert.Equal(20 * PointsToPixels * grid.Zoom, grid.EditorFontSize, 3);
    }
}
