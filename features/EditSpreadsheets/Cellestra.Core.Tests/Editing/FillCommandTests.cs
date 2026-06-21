using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class FillCommandTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    [Fact]
    public void Fill_down_copies_a_single_value()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(5)); // A1

        history.Execute(new FillCommand(sheet.Name, CellRange.Parse("A1"), CellRange.Parse("A1:A4")));

        Assert.Equal(5, sheet.GetValue(1, 0).AsNumber);
        Assert.Equal(5, sheet.GetValue(2, 0).AsNumber);
        Assert.Equal(5, sheet.GetValue(3, 0).AsNumber);
    }

    [Fact]
    public void Fill_down_shifts_relative_references()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10)); // A1
        sheet.SetFormula(0, 1, "A1*2", CellValue.Number(20)); // B1 = A1*2

        history.Execute(new FillCommand(sheet.Name, CellRange.Parse("B1"), CellRange.Parse("B1:B3")));

        Assert.Equal("A2*2", sheet.Find(1, 1)!.Formula);
        Assert.Equal("A3*2", sheet.Find(2, 1)!.Formula);
    }

    [Fact]
    public void Fill_tiles_a_multi_cell_source_pattern()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(1)); // A1
        sheet.SetValue(1, 0, CellValue.Number(2)); // A2

        history.Execute(new FillCommand(sheet.Name, CellRange.Parse("A1:A2"), CellRange.Parse("A1:A5")));

        Assert.Equal(1, sheet.GetValue(2, 0).AsNumber); // pattern repeats (no series)
        Assert.Equal(2, sheet.GetValue(3, 0).AsNumber);
        Assert.Equal(1, sheet.GetValue(4, 0).AsNumber);
    }

    [Fact]
    public void Fill_is_undoable_and_leaves_the_source_untouched()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(5));

        history.Execute(new FillCommand(sheet.Name, CellRange.Parse("A1"), CellRange.Parse("A1:A3")));
        history.Undo();

        Assert.Equal(5, sheet.GetValue(0, 0).AsNumber); // source kept
        Assert.True(sheet.GetValue(1, 0).IsEmpty);      // fill reverted
        Assert.True(sheet.GetValue(2, 0).IsEmpty);
    }
}
