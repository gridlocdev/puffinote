using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class DataEditCommandTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    [Fact]
    public void Sort_is_undoable()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("H"));
        sheet.SetValue(1, 0, CellValue.Number(3));
        sheet.SetValue(2, 0, CellValue.Number(1));
        sheet.SetValue(3, 0, CellValue.Number(2));

        history.Execute(DataEditCommands.SortByColumn(sheet.Name, CellRange.Parse("A1:A4"), 0, ascending: true));
        Assert.Equal(1, sheet.GetValue(1, 0).AsNumber);
        Assert.Equal(3, sheet.GetValue(3, 0).AsNumber);

        history.Undo();
        Assert.Equal(3, sheet.GetValue(1, 0).AsNumber); // original order restored
    }

    [Fact]
    public void Header_column_styles_the_first_column_like_the_header_and_undoes()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("Team"));
        sheet.SetValue(0, 1, CellValue.Text("Pts"));
        sheet.SetValue(1, 0, CellValue.Text("A"));
        sheet.SetValue(1, 1, CellValue.Number(3));
        sheet.SetValue(2, 0, CellValue.Text("B"));
        sheet.SetValue(2, 1, CellValue.Number(1));
        var table = new Table("T", CellRange.Parse("A1:B3"), HasHeader: true);
        sheet.AddTable(table);

        history.Execute(DataEditCommands.SetTableHeaderColumn(workbook, sheet.Name, table, enabled: true));

        Assert.True(sheet.Tables[0].HasHeaderColumn);
        // The first column's data cells (below the header row) are bolded — and nothing else: the
        // grey band is theme chrome the grid paints from the flag, never a fill baked into the cell.
        var a2 = workbook.Styles.Get(sheet.Find(1, 0)!.StyleId);
        Assert.True(a2.Font.Bold);
        Assert.False(a2.Fill.HasBackground);
        // A data cell in another column is untouched.
        Assert.False(workbook.Styles.Get(sheet.Find(1, 1)!.StyleId).Font.Bold);

        history.Undo();
        Assert.False(sheet.Tables[0].HasHeaderColumn);
        Assert.False(workbook.Styles.Get(sheet.Find(1, 0)!.StyleId).Font.Bold); // styling reverted
    }

    [Fact]
    public void Table_header_commands_bake_no_colours_into_cells()
    {
        // The header look is theme chrome — grey in light mode, dark grey in dark mode — so the one
        // thing these commands may write into the document is the bold emphasis. A baked fill or font
        // colour would freeze one theme's colours into the file.
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("Site"));
        sheet.SetValue(1, 0, CellValue.Text("All Sites"));
        var table = new Table("T", CellRange.Parse("A1:B2"), HasHeader: true);
        history.Execute(DataEditCommands.CreateTable(workbook, sheet.Name, table));
        history.Execute(DataEditCommands.SetTableHeaderColumn(workbook, sheet.Name, table, enabled: true));

        foreach (var (row, column) in new[] { (0, 0), (0, 1), (1, 0) })
        {
            var style = workbook.Styles.Get(sheet.Find(row, column)!.StyleId);
            Assert.True(style.Font.Bold);
            Assert.False(style.Fill.HasBackground);
        }
    }

    [Fact]
    public void Banded_rows_toggle_flips_the_flag_and_undoes()
    {
        // Banding is chrome drawn from the flag — off by default, and toggling it writes nothing
        // into any cell.
        var (_, history, sheet) = New();
        var table = new Table("T", CellRange.Parse("A1:B4"), HasHeader: true);
        sheet.AddTable(table);
        Assert.False(sheet.Tables[0].HasBandedRows);

        history.Execute(DataEditCommands.SetTableBandedRows(sheet.Name, table, enabled: true));
        Assert.True(sheet.Tables[0].HasBandedRows);
        Assert.Null(sheet.Find(1, 0)); // no cell was created, let alone styled

        history.Undo();
        Assert.False(sheet.Tables[0].HasBandedRows);
    }

    [Fact]
    public void Header_column_can_be_cleared_back_to_default()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("Team"));
        sheet.SetValue(1, 0, CellValue.Text("A"));
        var table = new Table("T", CellRange.Parse("A1:A2"), HasHeader: true);
        sheet.AddTable(table);

        history.Execute(DataEditCommands.SetTableHeaderColumn(workbook, sheet.Name, table, enabled: true));
        var on = sheet.Tables[0];
        history.Execute(DataEditCommands.SetTableHeaderColumn(workbook, sheet.Name, on, enabled: false));

        Assert.False(sheet.Tables[0].HasHeaderColumn);
        var a2 = workbook.Styles.Get(sheet.Find(1, 0)!.StyleId);
        Assert.False(a2.Font.Bold);
        Assert.False(a2.Fill.HasBackground); // fill cleared
    }

    [Fact]
    public void Filter_hides_rows_and_undo_restores()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("Score"));
        sheet.SetValue(1, 0, CellValue.Number(10));
        sheet.SetValue(2, 0, CellValue.Number(90));

        var spec = new FilterSpec(new Dictionary<int, ColumnFilter> { [0] = new ColumnFilter { Condition = ">40" } });
        history.Execute(DataEditCommands.ApplyFilter(sheet.Name, CellRange.Parse("A1:A3"), spec, CultureInfo.InvariantCulture));
        Assert.True(sheet.IsRowHidden(1));
        Assert.False(sheet.IsRowHidden(2));

        history.Undo();
        Assert.False(sheet.IsRowHidden(1));
    }

    [Fact]
    public void ResetSheetLayout_clears_row_column_fills_hidden_and_filter_and_undo_restores()
    {
        var (workbook, history, sheet) = New();

        // A column fill (background), a hidden row, and an active AutoFilter — the sheet-level state a
        // plain ClearAll over the used range can't reach.
        int fillId = workbook.Styles.Intern(CellStyle.Default
            .WithFill(Fill.Solid(ArgbColor.FromRgb(255, 0, 0))));
        sheet.GetOrCreateColumn(1).StyleId = fillId;
        sheet.GetOrCreateRow(2).Hidden = true;
        sheet.ActiveFilter = CellRange.Parse("A1:B3");
        sheet.ActiveFilterSpec = new FilterSpec(new Dictionary<int, ColumnFilter> { [0] = new ColumnFilter { Condition = ">0" } });

        history.Execute(DataEditCommands.ResetSheetLayout(sheet.Name));

        Assert.Equal(0, sheet.GetOrCreateColumn(1).StyleId); // fill cleared
        Assert.False(sheet.IsRowHidden(2));                  // unhidden
        Assert.Null(sheet.ActiveFilter);                     // filter dropped
        Assert.Null(sheet.ActiveFilterSpec);

        history.Undo();

        Assert.Equal(fillId, sheet.GetOrCreateColumn(1).StyleId);
        Assert.True(sheet.IsRowHidden(2));
        Assert.Equal(CellRange.Parse("A1:B3"), sheet.ActiveFilter);
        Assert.NotNull(sheet.ActiveFilterSpec);
    }

    [Fact]
    public void Remove_duplicates_is_undoable_and_reports_count()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("H"));
        sheet.SetValue(1, 0, CellValue.Text("a"));
        sheet.SetValue(2, 0, CellValue.Text("a"));
        sheet.SetValue(3, 0, CellValue.Text("b"));

        var command = DataEditCommands.RemoveDuplicates(sheet.Name, CellRange.Parse("A1:A4"), [0], true, out var removed);
        history.Execute(command);

        Assert.Equal(1, removed());
        Assert.Equal("b", sheet.GetValue(2, 0).AsText);

        history.Undo();
        Assert.Equal("a", sheet.GetValue(2, 0).AsText);
    }

    [Fact]
    public void Text_to_columns_is_undoable()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("a,b,c"));

        history.Execute(DataEditCommands.TextToColumns(sheet, CellRange.Parse("A1:A1"), ','));
        Assert.Equal("b", sheet.GetValue(0, 1).AsText);

        history.Undo();
        Assert.True(sheet.GetValue(0, 1).IsEmpty);
        Assert.Equal("a,b,c", sheet.GetValue(0, 0).AsText);
    }

    [Fact]
    public void Create_table_is_undoable()
    {
        var (_, history, sheet) = New();
        var table = new Table("Data", CellRange.Parse("A1:B3"));

        history.Execute(DataEditCommands.CreateTable(sheet.Name, table));
        Assert.NotNull(sheet.FindTable(0, 0));

        history.Undo();
        Assert.Null(sheet.FindTable(0, 0));
    }

    [Fact]
    public void Create_table_styles_header_row_with_contrasting_default()
    {
        var (workbook, history, sheet) = New();
        var table = new Table("Data", CellRange.Parse("A1:B3"), HasHeader: true);

        history.Execute(DataEditCommands.CreateTable(workbook, sheet.Name, table));

        // Header cells (row 0) are bolded and nothing more — the grey band is theme chrome the grid
        // paints, never a fill baked into the cell. The body stays default.
        foreach (int column in new[] { 0, 1 })
        {
            var header = workbook.Styles.Get(sheet.GetOrCreate(0, column).StyleId);
            Assert.True(header.Font.Bold);
            Assert.False(header.Fill.HasBackground);
        }

        Assert.False(workbook.Styles.Get(sheet.GetOrCreate(1, 0).StyleId).Font.Bold);

        history.Undo();
        Assert.Null(sheet.FindTable(0, 0));
        Assert.Equal(Cellestra.Core.Styling.StylePool.DefaultId, sheet.GetOrCreate(0, 0).StyleId);
    }

    [Fact]
    public void Create_table_prefills_empty_header_cells_left_to_right()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 1, CellValue.Text("Name")); // B1 already labelled; A1/C1 are empty
        var table = new Table("Data", CellRange.Parse("A1:C3"), HasHeader: true);

        history.Execute(DataEditCommands.CreateTable(workbook, sheet.Name, table));

        Assert.Equal("Column 1", sheet.GetValue(0, 0).AsText); // A1 placeholder
        Assert.Equal("Name", sheet.GetValue(0, 1).AsText);     // B1 left as typed
        Assert.Equal("Column 3", sheet.GetValue(0, 2).AsText); // C1 placeholder, numbered by position
        Assert.True(workbook.Styles.Get(sheet.GetOrCreate(0, 0).StyleId).Font.Bold);

        history.Undo();
        Assert.True(sheet.GetValue(0, 0).IsEmpty); // placeholder removed
        Assert.Equal("Name", sheet.GetValue(0, 1).AsText);
    }

    [Fact]
    public void Create_table_without_header_skips_styling()
    {
        var (workbook, history, sheet) = New();
        var table = new Table("Data", CellRange.Parse("A1:B3"), HasHeader: false);

        history.Execute(DataEditCommands.CreateTable(workbook, sheet.Name, table));

        Assert.Equal(Cellestra.Core.Styling.StylePool.DefaultId, sheet.GetOrCreate(0, 0).StyleId);
    }

    [Fact]
    public void Expand_table_for_edit_below_grows_the_range_and_is_undoable()
    {
        var (workbook, history, sheet) = New();
        var table = new Table("Data", CellRange.Parse("A1:B3"), HasHeader: true);
        history.Execute(DataEditCommands.CreateTable(workbook, sheet.Name, table));

        // An edit in the row directly below the table (row 3, within columns A–B) grows it down.
        var command = DataEditCommands.ExpandTableForEdit(workbook, sheet.Name, table, editedRow: 3, editedColumn: 0);
        Assert.NotNull(command);
        history.Execute(command!);

        Assert.Equal(CellRange.Parse("A1:B4"), Assert.Single(sheet.Tables).Range);

        history.Undo();
        Assert.Equal(CellRange.Parse("A1:B3"), Assert.Single(sheet.Tables).Range);
    }

    [Fact]
    public void Expand_table_for_edit_right_grows_range_and_styles_new_header()
    {
        var (workbook, history, sheet) = New();
        var table = new Table("Data", CellRange.Parse("A1:B3"), HasHeader: true);
        history.Execute(DataEditCommands.CreateTable(workbook, sheet.Name, table));

        // An edit in the column directly to the right (column C, within rows 1–3) grows it right.
        var command = DataEditCommands.ExpandTableForEdit(workbook, sheet.Name, table, editedRow: 0, editedColumn: 2);
        Assert.NotNull(command);
        history.Execute(command!);

        Assert.Equal(CellRange.Parse("A1:C3"), Assert.Single(sheet.Tables).Range);

        // The new header cell C1 gets the header emphasis (bold; the band itself is theme chrome).
        var header = workbook.Styles.Get(sheet.GetOrCreate(0, 2).StyleId);
        Assert.True(header.Font.Bold);
        Assert.False(header.Fill.HasBackground);

        history.Undo();
        Assert.Equal(CellRange.Parse("A1:B3"), Assert.Single(sheet.Tables).Range);
        Assert.Equal(Cellestra.Core.Styling.StylePool.DefaultId, sheet.GetOrCreate(0, 2).StyleId);
    }

    [Fact]
    public void Expand_table_for_edit_below_carries_the_header_column_onto_the_new_row()
    {
        // The mirror of the header-row case above, read along the other axis: a table with a header
        // column that grows downward has to embolden the new row's first cell, or the row labels stop
        // one row short of the table they label.
        var (workbook, history, sheet) = New();
        var table = new Table("Data", CellRange.Parse("A1:B3"), HasHeader: true) { HasHeaderColumn = true };
        history.Execute(DataEditCommands.CreateTable(workbook, sheet.Name, table));

        var command = DataEditCommands.ExpandTableForEdit(workbook, sheet.Name, table, editedRow: 3, editedColumn: 1);
        Assert.NotNull(command);
        history.Execute(command!);

        var header = workbook.Styles.Get(sheet.GetOrCreate(3, 0).StyleId);
        Assert.True(header.Font.Bold);
        Assert.False(header.Fill.HasBackground);

        history.Undo();
        Assert.Equal(Cellestra.Core.Styling.StylePool.DefaultId, sheet.GetOrCreate(3, 0).StyleId);
    }

    [Fact]
    public void Expand_table_for_edit_below_leaves_the_first_cell_plain_without_a_header_column()
    {
        var (workbook, history, sheet) = New();
        var table = new Table("Data", CellRange.Parse("A1:B3"), HasHeader: true);
        history.Execute(DataEditCommands.CreateTable(workbook, sheet.Name, table));

        history.Execute(DataEditCommands.ExpandTableForEdit(workbook, sheet.Name, table, editedRow: 3, editedColumn: 1)!);

        Assert.Equal(Cellestra.Core.Styling.StylePool.DefaultId, sheet.GetOrCreate(3, 0).StyleId);
    }

    [Fact]
    public void Expand_table_for_edit_returns_null_when_not_adjacent()
    {
        var (workbook, _, sheet) = New();
        var table = new Table("Data", CellRange.Parse("A1:B3"), HasHeader: true);

        Assert.Null(DataEditCommands.ExpandTableForEdit(workbook, sheet.Name, table, editedRow: 10, editedColumn: 10));
    }

    [Fact]
    public void Set_validation_is_undoable()
    {
        var (_, history, sheet) = New();
        var rule = new DataValidation(ValidationKind.List, CellRange.Parse("A1")) { ListValues = ["Yes", "No"] };

        history.Execute(DataEditCommands.SetValidation(sheet.Name, rule));
        Assert.NotNull(sheet.FindValidation(0, 0));

        history.Undo();
        Assert.Null(sheet.FindValidation(0, 0));
    }
}
