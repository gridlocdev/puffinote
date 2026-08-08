using System.Diagnostics;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class ClearCommandTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    [Fact]
    public void Clear_formatting_resets_style_but_keeps_content()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(42));
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true))));
        Assert.NotEqual(StylePool.DefaultId, sheet.Find(0, 0)!.StyleId);

        history.Execute(EditCommands.ClearFormatting(workbook, sheet.Name, CellRange.Parse("A1")));

        Assert.Equal(StylePool.DefaultId, sheet.Find(0, 0)!.StyleId); // formatting gone
        Assert.Equal(42, sheet.GetValue(0, 0).AsNumber);              // content stays

        history.Undo();
        Assert.True(workbook.Styles.Get(sheet.Find(0, 0)!.StyleId).Font.Bold); // restored
    }

    [Fact]
    public void Clear_all_drops_content_formula_style_and_hyperlink()
    {
        var (workbook, history, sheet) = New();
        sheet.SetFormula(0, 0, "1+1", CellValue.Number(2));
        var cell = sheet.Find(0, 0)!;
        cell.Hyperlink = new Hyperlink("https://example.com");
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true))));

        history.Execute(EditCommands.ClearAll(sheet.Name, CellRange.Parse("A1")));

        Assert.Null(sheet.Find(0, 0)); // cell pruned entirely

        history.Undo();
        var restored = sheet.Find(0, 0)!;
        Assert.Equal("1+1", restored.Formula);
        Assert.NotNull(restored.Hyperlink);
        Assert.True(workbook.Styles.Get(restored.StyleId).Font.Bold);
    }

    private static CellRange WholeSheet =>
        new(new CellAddress(0, 0), new CellAddress(SheetDimensions.MaxColumns - 1, SheetDimensions.MaxRows - 1));

    [Fact]
    public void Clear_all_on_whole_sheet_is_bounded_to_the_used_range_and_completes_quickly()
    {
        // The expanding-⌘A "whole sheet" selection is 16384×1048576 ≈ 17 billion cells. A clear
        // that materialized every position would hang/crash (the reported bug). Clamping to the
        // used range must keep it instant on a tiny populated sheet.
        var (_, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetValue(2, 3, CellValue.Text("x"));

        var watch = Stopwatch.StartNew();
        history.Execute(EditCommands.ClearAll(sheet.Name, WholeSheet));
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 2000, $"whole-sheet clear took {watch.ElapsedMilliseconds}ms");
        Assert.Null(sheet.Find(0, 0));
        Assert.Null(sheet.Find(2, 3));

        history.Undo(); // bounded undo restores exactly what was cleared
        Assert.Equal(1, sheet.GetValue(0, 0).AsNumber);
        Assert.Equal("x", sheet.GetValue(2, 3).AsText);
    }

    [Fact]
    public void Clear_contents_on_whole_sheet_keeps_styles_and_is_bounded()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(5, 5, CellValue.Number(7));
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("F6"),
            s => s.WithFont(s.Font.WithBold(true))));

        var watch = Stopwatch.StartNew();
        history.Execute(EditCommands.ClearContents(sheet.Name, WholeSheet));
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 2000, $"whole-sheet clear-contents took {watch.ElapsedMilliseconds}ms");
        Assert.True(sheet.GetValue(5, 5).IsEmpty);
        Assert.True(workbook.Styles.Get(sheet.Find(5, 5)!.StyleId).Font.Bold); // style kept
    }

    [Fact]
    public void Apply_style_on_whole_sheet_is_recorded_per_column_not_per_cell()
    {
        // A whole-sheet selection spans every row, so it is a whole-column format: it lands on the
        // columns' default cell styles rather than on 17 billion positions. The cell reads as bold
        // because it inherits, and — the point of the exercise — the sheet is not now "used" from
        // A1 to the bottom-right corner of the grid.
        var (workbook, history, sheet) = New();
        sheet.SetValue(1, 1, CellValue.Number(9));

        var watch = Stopwatch.StartNew();
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, WholeSheet,
            s => s.WithFont(s.Font.WithBold(true)), "Bold"));
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 2000, $"whole-sheet style took {watch.ElapsedMilliseconds}ms");
        Assert.True(workbook.Styles.Get(sheet.EffectiveStyleId(1, 1)).Font.Bold);
        Assert.Single(sheet.EnumerateCells()); // no cell was materialized to carry the format

        history.Undo();
        Assert.False(workbook.Styles.Get(sheet.EffectiveStyleId(1, 1)).Font.Bold);
    }
}
