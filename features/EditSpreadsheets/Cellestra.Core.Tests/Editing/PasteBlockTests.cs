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
    public void Cut_paste_makes_external_references_follow_the_moved_cell()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10));            // A1
        sheet.SetFormula(0, 2, "A1*2", CellValue.Number(20));  // C1 = A1*2 (points at A1)

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("A1"), isCut: true);
        history.Execute(new PasteBlockCommand(sheet.Name, 4, 0, block)); // move A1 → A5

        Assert.Equal("A5*2", sheet.Find(0, 2)!.Formula); // the reference followed the cell

        history.Undo();
        Assert.Equal("A1*2", sheet.Find(0, 2)!.Formula); // and is restored on undo
    }

    [Fact]
    public void Cut_paste_follows_even_absolute_references()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10));               // A1
        sheet.SetFormula(0, 2, "$A$1*2", CellValue.Number(20));   // C1 = $A$1*2

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("A1"), isCut: true);
        history.Execute(new PasteBlockCommand(sheet.Name, 4, 0, block)); // move A1 → A5

        // A move relocates the cell itself, so an absolute reference follows it (unlike a copy).
        Assert.Equal("$A$5*2", sheet.Find(0, 2)!.Formula);
    }

    [Fact]
    public void Cut_paste_adjusts_references_between_cells_that_move_together()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 1, CellValue.Number(3));               // B1
        sheet.SetFormula(1, 1, "B1+1", CellValue.Number(4));     // B2 = B1+1 (both inside the block)

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("B1:B2"), isCut: true);
        history.Execute(new PasteBlockCommand(sheet.Name, 0, 3, block)); // move B1:B2 → D1:D2

        // The intra-block reference moves with the block: B2's "B1" becomes D2's "D1".
        Assert.Equal("D1+1", sheet.Find(1, 3)!.Formula);
    }

    [Fact]
    public void Cut_paste_leaves_references_outside_the_moved_cell_untouched()
    {
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10));               // A1 (stays put)
        sheet.SetFormula(0, 1, "A1*2", CellValue.Number(20));     // B1 = A1*2 (the cell we move)

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("B1"), isCut: true);
        history.Execute(new PasteBlockCommand(sheet.Name, 5, 5, block)); // move B1 → F6

        // The moved cell's own reference to the un-moved A1 stays verbatim.
        Assert.Equal("A1*2", sheet.Find(5, 5)!.Formula);
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

    // ---- Paste Special modes ---------------------------------------------

    [Fact]
    public void PasteValues_writes_value_without_formula_and_keeps_destination_style()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10)); // A1
        sheet.SetFormula(0, 1, "A1*2", CellValue.Number(20)); // B1 = A1*2, cached 20

        // Give the destination (B2) its own style so we can assert it's preserved.
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("B2"),
            s => s.WithFont(s.Font.WithItalic(true))));
        int destStyle = sheet.Find(1, 1)!.StyleId;

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("B1"), isCut: false);
        history.Execute(new PasteBlockCommand(sheet.Name, 1, 1, block, PasteMode.Values)); // into B2

        var dest = sheet.Find(1, 1)!;
        Assert.Null(dest.Formula);                 // no formula, just the computed value
        Assert.Equal(20, dest.Value.AsNumber);
        Assert.Equal(destStyle, dest.StyleId);     // destination style kept
    }

    [Fact]
    public void PasteFormulas_writes_shifted_formula_and_keeps_destination_style()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10)); // A1
        sheet.SetFormula(0, 1, "A1*2", CellValue.Number(20)); // B1 = A1*2

        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("B2"),
            s => s.WithFont(s.Font.WithItalic(true))));
        int destStyle = sheet.Find(1, 1)!.StyleId;

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("B1"), isCut: false);
        history.Execute(new PasteBlockCommand(sheet.Name, 1, 1, block, PasteMode.Formulas)); // into B2

        var dest = sheet.Find(1, 1)!;
        Assert.Equal("A2*2", dest.Formula);        // formula came across, reference shifted
        Assert.Equal(destStyle, dest.StyleId);     // destination style kept (no style paste)
    }

    [Fact]
    public void PasteFormats_applies_source_style_and_leaves_destination_value()
    {
        var (workbook, history, sheet) = New();
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true))));
        int sourceStyle = sheet.Find(0, 0)!.StyleId;
        sheet.SetValue(2, 2, CellValue.Number(99)); // C3 has a value, default style

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        history.Execute(new PasteBlockCommand(sheet.Name, 2, 2, block, PasteMode.Formats)); // into C3

        var dest = sheet.Find(2, 2)!;
        Assert.Equal(99, dest.Value.AsNumber);     // value untouched
        Assert.Equal(sourceStyle, dest.StyleId);   // formatting applied
    }

    [Fact]
    public void PasteSpecial_is_undoable()
    {
        var (_, history, sheet) = New();
        sheet.SetFormula(0, 1, "1+1", CellValue.Number(2)); // B1
        sheet.SetValue(1, 1, CellValue.Number(5));          // B2 (destination)

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("B1"), isCut: false);
        history.Execute(new PasteBlockCommand(sheet.Name, 1, 1, block, PasteMode.Values));
        Assert.Equal(2, sheet.GetValue(1, 1).AsNumber);

        history.Undo();
        Assert.Equal(5, sheet.GetValue(1, 1).AsNumber); // original destination value restored
    }
}
