using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

/// <summary>
/// Inserting/deleting rows or columns that intersect a table resizes the table along the edited
/// axis (PRD §8.10); undo restores the original range.
/// </summary>
public class StructuralTableTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    private static void Delete(EditHistory history, Worksheet sheet, StructuralAxis axis, int at, int count) =>
        history.Execute(new StructuralEditCommand(sheet.Name, axis, StructuralOperation.Delete, at, count));

    private static void Insert(EditHistory history, Worksheet sheet, StructuralAxis axis, int at, int count) =>
        history.Execute(new StructuralEditCommand(sheet.Name, axis, StructuralOperation.Insert, at, count));

    [Fact]
    public void Deleting_rows_inside_a_table_shrinks_it()
    {
        var (_, history, sheet) = New();
        sheet.AddTable(new Table("T", CellRange.Parse("A2:B10")));

        Delete(history, sheet, StructuralAxis.Row, at: 4, count: 3); // remove rows 5–7

        Assert.Equal(CellRange.Parse("A2:B7"), Assert.Single(sheet.Tables).Range);

        history.Undo();
        Assert.Equal(CellRange.Parse("A2:B10"), Assert.Single(sheet.Tables).Range);
    }

    [Fact]
    public void Deleting_columns_inside_a_table_shrinks_it()
    {
        var (_, history, sheet) = New();
        sheet.AddTable(new Table("T", CellRange.Parse("B1:F5")));

        Delete(history, sheet, StructuralAxis.Column, at: 2, count: 2); // remove columns C–D

        Assert.Equal(CellRange.Parse("B1:D5"), Assert.Single(sheet.Tables).Range);
    }

    [Fact]
    public void Deleting_rows_above_a_table_shifts_it_up_unchanged_in_size()
    {
        var (_, history, sheet) = New();
        sheet.AddTable(new Table("T", CellRange.Parse("A5:B8")));

        Delete(history, sheet, StructuralAxis.Row, at: 0, count: 2); // remove rows 1–2 (above)

        Assert.Equal(CellRange.Parse("A3:B6"), Assert.Single(sheet.Tables).Range);
    }

    [Fact]
    public void Deleting_rows_below_a_table_leaves_it_untouched()
    {
        var (_, history, sheet) = New();
        sheet.AddTable(new Table("T", CellRange.Parse("A1:B4")));

        Delete(history, sheet, StructuralAxis.Row, at: 6, count: 2); // remove rows 7–8 (below)

        Assert.Equal(CellRange.Parse("A1:B4"), Assert.Single(sheet.Tables).Range);
    }

    [Fact]
    public void Deleting_every_row_of_a_table_removes_it()
    {
        var (_, history, sheet) = New();
        sheet.AddTable(new Table("T", CellRange.Parse("A3:B5")));

        Delete(history, sheet, StructuralAxis.Row, at: 2, count: 4); // covers rows 3–6 ⊇ 3–5

        Assert.Empty(sheet.Tables);

        history.Undo();
        Assert.Equal(CellRange.Parse("A3:B5"), Assert.Single(sheet.Tables).Range);
    }

    [Fact]
    public void Inserting_rows_inside_a_table_grows_it()
    {
        var (_, history, sheet) = New();
        sheet.AddTable(new Table("T", CellRange.Parse("A2:B6")));

        Insert(history, sheet, StructuralAxis.Row, at: 4, count: 2); // insert 2 rows mid-table

        Assert.Equal(CellRange.Parse("A2:B8"), Assert.Single(sheet.Tables).Range);
    }
}
