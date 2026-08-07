using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// How wide a column decides to be. A wrapped cell has no vote — it was told to fold at the column's
/// edge, so measuring it flat asks the column to be as wide as a paragraph. A table's columns keep up
/// with what you type into them, and "Auto-fit Columns" fits the whole table rather than one column.
/// </summary>
public class ColumnAutofitTests
{
    private const string Long = "a value far longer than the default column width can hold";

    private static MainWindow Open()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        return window;
    }

    private static void Wrap(IAppEnvironment env, string range) =>
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, env.Session.ActiveSheet.Name,
            CellRange.Parse(range), s => s with { Alignment = s.Alignment with { WrapText = true } }));

    [AvaloniaFact]
    public void A_wrapped_cell_does_not_drag_the_column_out_to_its_full_length()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("short"));
        sheet.SetValue(1, 0, CellValue.Text(Long));
        Wrap(env, "A2");

        env.FitColumns(0, 0);
        double withWrapped = sheet.GetColumnWidth(0);

        // The same sheet with the long value *not* wrapped: that one does get a say.
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, sheet.Name, CellRange.Parse("A2"),
            s => s with { Alignment = s.Alignment with { WrapText = false } }));
        env.FitColumns(0, 0);

        Assert.True(sheet.GetColumnWidth(0) > withWrapped,
            $"the unwrapped value should widen the column further ({withWrapped} → {sheet.GetColumnWidth(0)})");
    }

    [AvaloniaFact]
    public void A_column_of_nothing_but_wrapped_cells_keeps_a_sensible_width()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        env.Session.ActiveSheet.SetValue(0, 0, CellValue.Text(Long));
        Wrap(env, "A1");

        env.FitColumns(0, 0);

        Assert.Equal(Cellestra.Core.Defaults.ColumnWidth, env.Session.ActiveSheet.GetColumnWidth(0), 1);
    }

    [AvaloniaFact]
    public void Editing_inside_a_table_widens_its_column_to_fit()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(1, 0, CellValue.Text("a"));
        sheet.AddTable(new Table("T", CellRange.Parse("A1:A2"), HasHeader: true));
        window.Editor.Grid.Refresh();

        double before = sheet.GetColumnWidth(0);
        env.Selection.MoveTo(1, 0);
        window.Editor.Grid.BeginEdit(Long);
        window.Editor.Grid.CommitEditForTest();

        Assert.True(sheet.GetColumnWidth(0) > before, $"the table column should grow ({before} → {sheet.GetColumnWidth(0)})");
    }

    [AvaloniaFact]
    public void The_same_edit_outside_a_table_leaves_the_column_alone()
    {
        // Only a table is a declared shape. A bare grid stays exactly as wide as you set it.
        var window = Open();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        double before = sheet.GetColumnWidth(0);
        env.Selection.MoveTo(1, 0);
        window.Editor.Grid.BeginEdit(Long);
        window.Editor.Grid.CommitEditForTest();

        Assert.Equal(before, sheet.GetColumnWidth(0), 1);
    }

    [AvaloniaFact]
    public void Auto_fit_columns_fits_the_whole_table_from_one_cell_in_it()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Notes"));
        sheet.SetValue(1, 0, CellValue.Text("a"));
        sheet.SetValue(1, 1, CellValue.Text(Long));
        sheet.AddTable(new Table("T", CellRange.Parse("A1:B2"), HasHeader: true));
        window.Editor.Grid.Refresh();

        env.Selection.MoveTo(0, 0); // one cell of the table, in the *narrow* column
        window.Editor.Commands.ById("data.autofitColumns")!.Execute();

        Assert.True(sheet.GetColumnWidth(1) > Cellestra.Core.Defaults.ColumnWidth,
            "the second column of the table should have been fitted too");
    }
}
