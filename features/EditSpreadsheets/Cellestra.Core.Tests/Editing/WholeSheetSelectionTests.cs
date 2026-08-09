using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

/// <summary>
/// What happens after ⌘A has expanded to the whole sheet (PRD §10.1). That selection is a rectangle of
/// 16384 × 1048576 — a number no operation may turn into one entry per cell, which is exactly how Excel
/// and Calc hold it too (Calc keeps a marked <em>range</em>, <c>ScMarkData::aMarkRange</c>, and paints
/// only the viewport). The in-place edits already clamp to the used range; these are the two paths that
/// did not, and both of them ended in a loop with no end anyone would wait for rather than an error.
/// </summary>
public class WholeSheetSelectionTests
{
    private static CellRange WholeSheet => new(
        new CellAddress(0, 0),
        new CellAddress(SheetDimensions.MaxColumns - 1, SheetDimensions.MaxRows - 1));

    private static Worksheet SheetWithABlock()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Number(1)); // A1
        sheet.SetValue(0, 1, CellValue.Number(2)); // B1
        sheet.SetValue(1, 0, CellValue.Number(3)); // A2
        sheet.SetValue(1, 1, CellValue.Number(4)); // B2
        return sheet;
    }

    /// <summary>
    /// A copy of the whole sheet is a copy of what is on it. The array this used to allocate had 17 billion
    /// slots; the assertion that matters is the shape, and that the test returns at all.
    /// </summary>
    [Fact]
    public void Copying_the_whole_sheet_captures_only_the_used_range()
    {
        var sheet = SheetWithABlock();

        var block = ClipboardBlock.Capture(sheet, WholeSheet, isCut: false);

        Assert.Equal(2, block.Rows);
        Assert.Equal(2, block.Columns);
        Assert.Equal(CellRange.Parse("A1:B2"), block.SourceRange);
        Assert.Equal(4d, block.Cells[1, 1]!.Value.AsNumber);
    }

    /// <summary>An empty sheet has no used range; the copy is one blank cell, not a crash and not the sheet.</summary>
    [Fact]
    public void Copying_the_whole_sheet_of_an_empty_sheet_captures_one_cell()
    {
        var block = ClipboardBlock.Capture(new Worksheet("Sheet1"), WholeSheet, isCut: false);

        Assert.Equal(1, block.Rows);
        Assert.Equal(1, block.Columns);
        Assert.Null(block.Cells[0, 0]);
    }

    /// <summary>
    /// A selection someone actually drew is taken at face value, blanks included — the clamp is for the
    /// sheet-sized rectangle ⌘A produces, not for a large-ish range.
    /// </summary>
    [Fact]
    public void A_deliberately_selected_block_of_blanks_still_copies_as_blanks()
    {
        var sheet = SheetWithABlock();

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("D1:E3"), isCut: false);

        Assert.Equal(3, block.Rows);
        Assert.Equal(2, block.Columns);
        Assert.Null(block.Cells[0, 0]);
    }

    /// <summary>
    /// One cell pasted into the whole sheet: Excel really does write all 17 billion, and we do not. The
    /// paste anchors at the top-left, which is bounded, undoable, and returns.
    /// </summary>
    [Fact]
    public void Filling_the_whole_sheet_from_one_cell_anchors_instead()
    {
        var plan = PastePlanner.Plan(blockRows: 1, blockColumns: 1, WholeSheet, isCut: false);

        Assert.Single(plan.Anchors);
        Assert.Equal((0, 0), plan.Anchors[0]);
        Assert.False(plan.Tiled);
        Assert.False(plan.ShapeMismatch);
    }

    /// <summary>The whole sheet is an exact multiple of almost any block, so tiling is bounded the same way.</summary>
    [Fact]
    public void Tiling_into_the_whole_sheet_anchors_instead()
    {
        var plan = PastePlanner.Plan(blockRows: 2, blockColumns: 2, WholeSheet, isCut: false);

        Assert.Single(plan.Anchors);
        Assert.False(plan.Tiled);
    }

    /// <summary>An ordinary fill is untouched: one cell still floods the range someone selected by hand.</summary>
    [Fact]
    public void An_ordinary_fill_still_covers_every_selected_cell()
    {
        var plan = PastePlanner.Plan(blockRows: 1, blockColumns: 1, CellRange.Parse("A1:C2"), isCut: false);

        Assert.Equal(6, plan.Anchors.Count);
        Assert.True(plan.Tiled);
    }
}
