using Cellestra.Core;
using Cellestra.Core.Values;
using Cellestra.Core.Model;
using Xunit;

namespace Cellestra.Core.Tests.Model;

public class WorkbookModelTests
{
    [Fact]
    public void CreateNew_has_one_sheet_named_Sheet1()
    {
        var workbook = Workbook.CreateNew();

        Assert.Equal(1, workbook.SheetCount);
        Assert.Equal("Sheet1", workbook.ActiveSheet.Name);
    }

    [Fact]
    public void AddSheet_auto_names_next_unused_number()
    {
        var workbook = Workbook.CreateNew();

        Assert.Equal("Sheet2", workbook.AddSheet().Name);
        Assert.Equal("Sheet3", workbook.AddSheet().Name);
    }

    [Fact]
    public void AddSheet_skips_taken_numbers()
    {
        var workbook = Workbook.CreateNew();
        workbook.AddSheet("Sheet2");

        Assert.Equal("Sheet3", workbook.AddSheet().Name);
    }

    [Fact]
    public void Cannot_remove_the_last_sheet()
    {
        var workbook = Workbook.CreateNew();

        Assert.Throws<InvalidOperationException>(() => { workbook.RemoveSheet(workbook.ActiveSheet); });
    }

    [Fact]
    public void Worksheet_stores_only_non_blank_cells()
    {
        var sheet = new Worksheet("S");

        sheet.SetValue(0, 0, CellValue.Number(42));
        Assert.Equal(1, sheet.CellCount);

        sheet.SetValue(0, 0, CellValue.Empty);
        Assert.Equal(0, sheet.CellCount);
    }

    [Fact]
    public void GetValue_of_blank_cell_is_empty()
    {
        var sheet = new Worksheet("S");

        Assert.True(sheet.GetValue(10, 10).IsEmpty);
    }

    [Fact]
    public void SetFormula_keeps_cached_value()
    {
        var sheet = new Worksheet("S");

        sheet.SetFormula(0, 0, "SUM(A2:A3)", CellValue.Number(7));

        var cell = sheet.Find(0, 0)!;
        Assert.True(cell.IsFormula);
        Assert.Equal("SUM(A2:A3)", cell.Formula);
        Assert.Equal(CellValue.Number(7), cell.Value);
    }

    [Fact]
    public void Merged_regions_reject_overlap()
    {
        var sheet = new Worksheet("S");
        sheet.AddMerge(new MergedRegion(CellRange.Parse("A1:B2")));

        Assert.Throws<InvalidOperationException>(
            () => sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3"))));
    }

    [Fact]
    public void GetUsedRange_covers_content_and_sizing()
    {
        var sheet = new Worksheet("S");
        sheet.SetValue(2, 3, CellValue.Text("x"));
        sheet.GetOrCreateColumn(5).Width = 100;

        var used = sheet.GetUsedRange();

        Assert.NotNull(used);
        Assert.Equal(2, used!.Value.Top);
        Assert.Equal(2, used.Value.Bottom);
        Assert.Equal(3, used.Value.Left);
        Assert.Equal(5, used.Value.Right);
    }

    [Fact]
    public void Named_ranges_resolve_sheet_scope_over_global()
    {
        var workbook = Workbook.CreateNew();
        workbook.AddNamedRange(new NamedRange("Rate", CellRange.Parse("A1")));
        workbook.AddNamedRange(new NamedRange("Rate", CellRange.Parse("B2")) { Scope = "Sheet1" });

        Assert.Equal(CellRange.Parse("B2"), workbook.FindNamedRange("Rate", "Sheet1")!.Target);
        Assert.Equal(CellRange.Parse("A1"), workbook.FindNamedRange("Rate")!.Target);
    }

    [Fact]
    public void Defaults_match_PRD_8_8()
    {
        Assert.Equal("Helvetica Neue", Defaults.FontFamily);
        Assert.Equal(10d, Defaults.FontSizePoints);
        Assert.Equal(64d, Defaults.ColumnWidth);
        Assert.Equal(20d, Defaults.RowHeight);
    }
}
