using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class SwapCellsTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    [Fact]
    public void Swap_exchanges_two_cells_contents()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10)); // A1
        sheet.SetValue(0, 1, CellValue.Number(20)); // B1

        history.Execute(new SwapCellsCommand(sheet.Name, CellRange.Parse("A1"), 0, 1)); // A1 ⇄ B1

        Assert.Equal(20d, sheet.Find(0, 0)!.Value.AsNumber);
        Assert.Equal(10d, sheet.Find(0, 1)!.Value.AsNumber);

        history.Undo();
        Assert.Equal(10d, sheet.Find(0, 0)!.Value.AsNumber);
        Assert.Equal(20d, sheet.Find(0, 1)!.Value.AsNumber);
    }

    [Fact]
    public void Swap_with_an_empty_destination_is_a_plain_move()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10)); // A1; B1 empty

        history.Execute(new SwapCellsCommand(sheet.Name, CellRange.Parse("A1"), 0, 1));

        Assert.Null(sheet.Find(0, 0));                       // A1 now holds B1's (empty) content
        Assert.Equal(10d, sheet.Find(0, 1)!.Value.AsNumber); // the value moved to B1
    }

    [Fact]
    public void Swap_makes_references_follow_both_ways_without_hijack()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10));            // A1
        sheet.SetValue(0, 1, CellValue.Number(20));            // B1
        sheet.SetFormula(0, 2, "A1", CellValue.Number(10));    // C1 = A1 (wants A1's content)
        sheet.SetFormula(0, 3, "B1", CellValue.Number(20));    // D1 = B1 (wants B1's content)

        history.Execute(new SwapCellsCommand(sheet.Name, CellRange.Parse("A1"), 0, 1)); // A1 ⇄ B1

        // Each formula's reference follows the data it pointed at to the cell's new home — so neither
        // is hijacked by the value that moved in.
        Assert.Equal("B1", sheet.Find(0, 2)!.Formula); // C1 followed A1's content to B1
        Assert.Equal("A1", sheet.Find(0, 3)!.Formula); // D1 followed B1's content to A1

        history.Undo();
        Assert.Equal("A1", sheet.Find(0, 2)!.Formula);
        Assert.Equal("B1", sheet.Find(0, 3)!.Formula);
    }

    [Fact]
    public void Swap_is_its_own_inverse()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10));         // A1
        sheet.SetValue(0, 1, CellValue.Number(20));         // B1
        sheet.SetFormula(0, 2, "A1+B1", CellValue.Number(30)); // C1

        history.Execute(new SwapCellsCommand(sheet.Name, CellRange.Parse("A1"), 0, 1));
        history.Execute(new SwapCellsCommand(sheet.Name, CellRange.Parse("A1"), 0, 1)); // swap back

        Assert.Equal(10d, sheet.Find(0, 0)!.Value.AsNumber);
        Assert.Equal(20d, sheet.Find(0, 1)!.Value.AsNumber);
        Assert.Equal("A1+B1", sheet.Find(0, 2)!.Formula); // references round-trip to the original
    }

    [Fact]
    public void Swapping_two_blocks_carries_intra_block_references_along()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(1));            // A1
        sheet.SetFormula(1, 0, "A1+1", CellValue.Number(2));  // A2 = A1+1 (both in block A)

        // Swap the A1:A2 block with the C1:C2 block (two columns over — disjoint).
        history.Execute(new SwapCellsCommand(sheet.Name, CellRange.Parse("A1:A2"), 0, 2));

        // A2's content moved to C2 and its reference to A1 followed to C1.
        Assert.Equal("C1+1", sheet.Find(1, 2)!.Formula);
    }
}
