using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
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
        var (workbook, history, sheet) = New();
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
    public void Filter_hides_rows_and_undo_restores()
    {
        var (workbook, history, sheet) = New();
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
    public void Remove_duplicates_is_undoable_and_reports_count()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("H"));
        sheet.SetValue(1, 0, CellValue.Text("a"));
        sheet.SetValue(2, 0, CellValue.Text("a"));
        sheet.SetValue(3, 0, CellValue.Text("b"));

        var command = DataEditCommands.RemoveDuplicates(sheet.Name, CellRange.Parse("A1:A4"), new[] { 0 }, true, out var removed);
        history.Execute(command);

        Assert.Equal(1, removed());
        Assert.Equal("b", sheet.GetValue(2, 0).AsText);

        history.Undo();
        Assert.Equal("a", sheet.GetValue(2, 0).AsText);
    }

    [Fact]
    public void Text_to_columns_is_undoable()
    {
        var (workbook, history, sheet) = New();
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
        var (workbook, history, sheet) = New();
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

        // Header cells (row 0) get the royal-blue accent fill + bold white text; the body stays default.
        foreach (int column in new[] { 0, 1 })
        {
            var header = workbook.Styles.Get(sheet.GetOrCreate(0, column).StyleId);
            Assert.Equal(Cellestra.Core.Styling.ArgbColor.FromHex("#1E4FB5"), header.Fill.Background);
            Assert.True(header.Font.Bold);
            Assert.Equal(Cellestra.Core.Styling.ArgbColor.White, header.Font.Color);
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

        // The new header cell C1 gets the table-header style (accent fill + bold white text).
        var header = workbook.Styles.Get(sheet.GetOrCreate(0, 2).StyleId);
        Assert.Equal(Cellestra.Core.Styling.ArgbColor.FromHex("#1E4FB5"), header.Fill.Background);
        Assert.True(header.Font.Bold);

        history.Undo();
        Assert.Equal(CellRange.Parse("A1:B3"), Assert.Single(sheet.Tables).Range);
        Assert.Equal(Cellestra.Core.Styling.StylePool.DefaultId, sheet.GetOrCreate(0, 2).StyleId);
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
        var (workbook, history, sheet) = New();
        var rule = new DataValidation(ValidationKind.List, CellRange.Parse("A1")) { ListValues = new[] { "Yes", "No" } };

        history.Execute(DataEditCommands.SetValidation(sheet.Name, rule));
        Assert.NotNull(sheet.FindValidation(0, 0));

        history.Undo();
        Assert.Null(sheet.FindValidation(0, 0));
    }
}
