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
