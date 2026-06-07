using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class PasteBlockTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    [Fact]
    public void Copy_paste_preserves_formula_and_style_and_shifts_references()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10)); // A1
        sheet.SetFormula(0, 1, "A1*2", CellValue.Number(20)); // B1 = A1*2

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("B1"), isCut: false);
        history.Execute(new PasteBlockCommand(sheet.Name, 1, 1, block)); // paste into B2

        // The formula came across and its relative reference shifted one row down.
        Assert.Equal("A2*2", sheet.Find(1, 1)!.Formula);
    }

    [Fact]
    public void Cut_paste_moves_cells_and_keeps_formula_unchanged()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10)); // A1
        sheet.SetFormula(0, 1, "A1*2", CellValue.Number(20)); // B1 = A1*2

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("B1"), isCut: true);
        history.Execute(new PasteBlockCommand(sheet.Name, 5, 5, block)); // move to F6

        Assert.Null(sheet.Find(0, 1));                       // source cleared
        Assert.Equal("A1*2", sheet.Find(5, 5)!.Formula);     // formula moved verbatim

        history.Undo();
        Assert.Equal("A1*2", sheet.Find(0, 1)!.Formula);     // source restored
        Assert.Null(sheet.Find(5, 5));                       // destination cleared
    }

    [Fact]
    public void Single_cell_fills_a_multi_cell_selection()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(7)); // A1

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        history.Execute(new PasteBlockCommand(sheet.Name, CellRange.Parse("C1:D2"), block));

        Assert.Equal(7, sheet.GetValue(0, 2).AsNumber);
        Assert.Equal(7, sheet.GetValue(0, 3).AsNumber);
        Assert.Equal(7, sheet.GetValue(1, 2).AsNumber);
        Assert.Equal(7, sheet.GetValue(1, 3).AsNumber); // whole selection filled
    }

    [Fact]
    public void Block_tiles_across_an_exact_multiple_selection()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(1)); // A1
        sheet.SetValue(0, 1, CellValue.Number(2)); // B1 — a 1x2 block

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("A1:B1"), isCut: false);
        history.Execute(new PasteBlockCommand(sheet.Name, CellRange.Parse("A3:D3"), block)); // 1x4 = two tiles

        Assert.Equal(1, sheet.GetValue(2, 0).AsNumber);
        Assert.Equal(2, sheet.GetValue(2, 1).AsNumber);
        Assert.Equal(1, sheet.GetValue(2, 2).AsNumber); // tiled again
        Assert.Equal(2, sheet.GetValue(2, 3).AsNumber);
    }

    [Fact]
    public void Mismatched_shapes_paste_once_and_flag()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetValue(0, 1, CellValue.Number(2)); // 1x2 block

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("A1:B1"), isCut: false);
        var command = new PasteBlockCommand(sheet.Name, CellRange.Parse("A3:C3"), block); // 1x3, not a multiple
        history.Execute(command);

        Assert.True(command.ShapeMismatch);
        Assert.Equal(1, sheet.GetValue(2, 0).AsNumber); // pasted once at the top-left
        Assert.Equal(2, sheet.GetValue(2, 1).AsNumber);
        Assert.True(sheet.GetValue(2, 2).IsEmpty);
    }

    [Fact]
    public void Paste_carries_the_style_index()
    {
        var (workbook, history, sheet) = New();
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true))));
        int styledId = sheet.Find(0, 0)!.StyleId;

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        history.Execute(new PasteBlockCommand(sheet.Name, 2, 2, block));

        Assert.Equal(styledId, sheet.Find(2, 2)!.StyleId);
    }
}
