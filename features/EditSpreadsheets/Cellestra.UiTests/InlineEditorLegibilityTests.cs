using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The inline editor paints the surface the cell is actually drawn over, and text you can read on it.
/// A table header styled with white text is the case that forced this: the header band it was chosen
/// against is the grid's chrome, not the cell's fill, so the editor used to open white-on-white and
/// the value you were editing simply vanished.
/// </summary>
public class InlineEditorLegibilityTests
{
    private static MainWindow TableWindow()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Team"));
        sheet.SetValue(1, 0, CellValue.Text("A"));
        sheet.AddTable(new Table("T", CellRange.Parse("A1:A2"), HasHeader: true));
        window.Editor.Grid.Refresh();
        return window;
    }

    [AvaloniaFact]
    public void White_header_text_is_readable_while_you_edit_it()
    {
        var window = TableWindow();
        IAppEnvironment env = window.Editor;
        var grid = window.Editor.Grid;

        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, env.Session.ActiveSheet.Name,
            CellRange.Parse("A1"), s => s with { Font = s.Font with { Color = ArgbColor.White } }));

        env.Selection.MoveTo(0, 0);
        grid.BeginEdit(null);

        // The editor sits on the table's header band, not a blank canvas...
        var fill = ArgbColor.FromArgb(grid.EditorFill.A, grid.EditorFill.R, grid.EditorFill.G, grid.EditorFill.B);
        var ink = ArgbColor.FromArgb(grid.EditorInk.A, grid.EditorInk.R, grid.EditorInk.G, grid.EditorInk.B);
        Assert.NotEqual(ArgbColor.White, fill);

        // ...and whatever it does paint, the text on it can be read.
        Assert.NotEqual(ArgbColor.White, ink);
        Assert.True(Contrast.Ratio(ink, fill) >= 3.0, $"ink {ink.ToHex()} on {fill.ToHex()} is unreadable");
    }

    [AvaloniaFact]
    public void Editing_inside_a_table_uses_light_ink_in_dark_mode()
    {
        // A table's header band and canvas are both dark surfaces in dark mode, and the header cells
        // carry no baked colours — so opening any table cell for editing must type in the theme's
        // light ink, not dark-on-dark.
        var window = TableWindow();
        window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        IAppEnvironment env = window.Editor;
        var grid = window.Editor.Grid;

        foreach (int row in new[] { 0, 1 }) // the header cell, then a data cell
        {
            env.Selection.MoveTo(row, 0);
            grid.BeginEdit(null);

            var fill = ArgbColor.FromArgb(grid.EditorFill.A, grid.EditorFill.R, grid.EditorFill.G, grid.EditorFill.B);
            var ink = ArgbColor.FromArgb(grid.EditorInk.A, grid.EditorInk.R, grid.EditorInk.G, grid.EditorInk.B);
            Assert.True(ink.R > 180, $"row {row}: dark-mode editor ink should be light, was {ink.ToHex()}");
            Assert.True(Contrast.Ratio(ink, fill) >= 3.0, $"row {row}: ink {ink.ToHex()} on {fill.ToHex()} is unreadable");

            grid.CommitEditForTest();
        }
    }

    [AvaloniaFact]
    public void A_readable_font_colour_is_left_exactly_as_the_document_chose_it()
    {
        // The guard only rescues ink that is genuinely invisible. A colour is document data, and quietly
        // "improving" a legible one would misreport what the file says.
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var grid = window.Editor.Grid;

        var chosen = ArgbColor.FromRgb(0xC0, 0x00, 0x00);
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, env.Session.ActiveSheet.Name,
            CellRange.Parse("A1"), s => s with { Font = s.Font with { Color = chosen } }));

        env.Selection.MoveTo(0, 0);
        grid.BeginEdit(null);

        Assert.Equal(chosen.R, grid.EditorInk.R);
        Assert.Equal(chosen.G, grid.EditorInk.G);
        Assert.Equal(chosen.B, grid.EditorInk.B);
    }
}
