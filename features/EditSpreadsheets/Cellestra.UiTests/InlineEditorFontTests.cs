using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
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
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        var grid = window.Editor.Grid;

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

    [AvaloniaFact]
    public void Formatting_commands_applied_while_editing_re_skin_the_live_editor()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var grid = window.Editor.Grid;

        env.Selection.MoveTo(0, 0);
        grid.BeginEdit("hello");
        Assert.False(grid.EditorIsBold);
        Assert.Equal(Avalonia.Media.TextAlignment.Left, grid.EditorTextAlignment);

        // Clicking Bold / Align Right mid-edit must immediately restyle the text being typed
        // (the toolbar buttons don't take focus, so the edit stays live).
        window.Editor.Commands.ById("format.bold")!.Execute();
        window.Editor.Commands.ById("format.alignRight")!.Execute();

        Assert.True(grid.EditorIsBold);
        Assert.Equal(Avalonia.Media.TextAlignment.Right, grid.EditorTextAlignment);
    }
}
