using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class ClearCommandTests
{
    [AvaloniaFact]
    public void Clear_formatting_command_resets_style_over_the_selection()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("keep"));
        env.Selection.MoveTo(0, 0);
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true))));

        AppCommands.ById("edit.clearFormats")!.Execute(env);

        Assert.Equal(StylePool.DefaultId, sheet.Find(0, 0)!.StyleId);
        Assert.Equal("keep", sheet.GetValue(0, 0).AsText);
    }

    [AvaloniaFact]
    public void Clear_all_command_empties_the_selection()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("gone"));
        env.Selection.MoveTo(0, 0);

        AppCommands.ById("edit.clearAll")!.Execute(env);

        Assert.Null(sheet.Find(0, 0));
    }

    [AvaloniaFact]
    public void Clear_all_removes_tables_and_filters_overlapping_the_selection()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Score"));
        env.Session.Execute(DataEditCommands.CreateTable(env.Session.Workbook, sheet.Name,
            new Cellestra.Core.Data.Table("Data", CellRange.Parse("A1:B3"))));
        sheet.ActiveFilter = CellRange.Parse("A1:B3");

        env.Selection.SetRange(0, 0, 2, 1); // select the whole table
        AppCommands.ById("edit.clearAll")!.Execute(env);

        Assert.Null(sheet.FindTable(0, 0));  // table (+ filter dropdowns + header fill) gone
        Assert.Null(sheet.ActiveFilter);     // filter gone
        Assert.Null(sheet.Find(0, 0));       // cells cleared

        env.Session.Undo();                  // single reversible step
        Assert.NotNull(sheet.FindTable(0, 0));
        Assert.Equal("Name", sheet.GetValue(0, 0).AsText);
    }

    [AvaloniaFact]
    public void Reset_sheet_data_clears_cell_fills_column_fills_and_filters()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        // A cell with a background fill, a whole-column fill, and an active AutoFilter — the things the
        // user reported survive a reset.
        sheet.SetValue(0, 0, CellValue.Text("Header"));
        int fillId = env.Session.Workbook.Styles.Intern(CellStyle.Default
            .WithFill(Fill.Solid(ArgbColor.FromRgb(255, 0, 0))));
        sheet.GetOrCreate(0, 0).StyleId = fillId;
        sheet.GetOrCreateColumn(1).StyleId = fillId;
        sheet.ActiveFilter = CellRange.Parse("A1:B1");

        env.ResetSheetData();

        Assert.Null(sheet.Find(0, 0));                          // cell + its fill gone
        Assert.Equal(StylePool.DefaultId, sheet.GetOrCreateColumn(1).StyleId); // column fill gone
        Assert.Null(sheet.ActiveFilter);                        // filter gone
    }

    [AvaloniaFact]
    public void Reset_sheet_data_removes_tables_their_filter_dropdowns_and_header_fill()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        // A ⌘T table: its header row gets a colored fill and it draws its own filter dropdowns — the
        // "background colors / filters" the user saw survive a reset (tables aren't in the used range).
        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Score"));
        env.Session.Execute(DataEditCommands.CreateTable(env.Session.Workbook, sheet.Name,
            new Cellestra.Core.Data.Table("Data", CellRange.Parse("A1:B3"))));
        Assert.NotNull(sheet.FindTable(0, 0));

        env.Selection.MoveTo(0, 0);
        env.ResetSheetData();

        Assert.Null(sheet.FindTable(0, 0));   // table (and its filter dropdowns) gone
        Assert.Empty(sheet.Tables);
        Assert.Null(sheet.Find(0, 0));        // header cell + its fill gone

        // Undo restores the table so the reset stays a single reversible step.
        env.Session.Undo();
        Assert.NotNull(sheet.FindTable(0, 0));
    }
}
