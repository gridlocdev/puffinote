using Cellestra.Core;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
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
    public void DuplicateSheet_inserts_an_independent_copy_right_after_the_original()
    {
        var workbook = Workbook.CreateNew();
        var source = workbook.ActiveSheet; // Sheet1
        source.SetValue(0, 0, CellValue.Text("hello"));
        source.FrozenRows = 1;
        workbook.AddSheet("After"); // a sheet after Sheet1, to prove the copy lands between them

        var copy = workbook.DuplicateSheet(source);

        Assert.Equal("Sheet1 copy", copy.Name);
        Assert.Equal(1, workbook.IndexOf(copy));            // immediately after the original
        Assert.Equal("hello", copy.GetValue(0, 0).AsText);  // content copied
        Assert.Equal(1, copy.FrozenRows);                    // layout copied

        // The copy is fully detached: editing it leaves the original untouched.
        copy.SetValue(0, 0, CellValue.Text("changed"));
        Assert.Equal("hello", source.GetValue(0, 0).AsText);
    }

    [Fact]
    public void DuplicateSheet_copies_a_chart_sheet_with_its_own_chart()
    {
        var workbook = Workbook.CreateNew();
        var chart = new Cellestra.Charts.ChartDefinition { Title = "Sales" };
        var source = workbook.AddChartSheet(chart);

        var copy = workbook.DuplicateSheet(source);

        Assert.True(copy.IsChartSheet);
        Assert.Equal("Sales", copy.Chart!.Title);
        Assert.NotSame(source.Chart, copy.Chart); // a deep copy, not a shared reference
    }

    [Fact]
    public void DuplicateSheet_renames_copied_tables_so_they_stay_unique()
    {
        var workbook = Workbook.CreateNew();
        var source = workbook.ActiveSheet;
        source.SetValue(0, 0, CellValue.Text("H"));
        source.SetValue(1, 0, CellValue.Number(1));
        source.AddTable(new Cellestra.Core.Data.Table("Sales", CellRange.Parse("A1:A2")));

        var copy = workbook.DuplicateSheet(source);

        Assert.Equal("Sales", source.Tables[0].Name);     // original keeps its name
        Assert.Equal("Sales_2", copy.Tables[0].Name);     // the copy's table is renamed unique
    }

    [Fact]
    public void DuplicateSheet_name_avoids_collisions()
    {
        var workbook = Workbook.CreateNew();
        var source = workbook.ActiveSheet;

        Assert.Equal("Sheet1 copy", workbook.DuplicateSheet(source).Name);
        Assert.Equal("Sheet1 copy 2", workbook.DuplicateSheet(source).Name);
    }

    [Fact]
    public void Cannot_remove_the_last_sheet()
    {
        var workbook = Workbook.CreateNew();

        Assert.Throws<InvalidOperationException>(() => { workbook.RemoveSheet(workbook.ActiveSheet); });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Has/Slash")]
    [InlineData("Bad:Colon")]
    [InlineData("Q?")]
    [InlineData("Star*")]
    [InlineData("Bracket[1]")]
    [InlineData("'quoted'")]
    public void AddSheet_rejects_invalid_names(string name)
    {
        var workbook = Workbook.CreateNew();

        Assert.Throws<ArgumentException>(() => workbook.AddSheet(name));
    }

    [Fact]
    public void AddSheet_rejects_names_over_the_length_cap()
    {
        var workbook = Workbook.CreateNew();

        Assert.Throws<ArgumentException>(() => workbook.AddSheet(new string('x', SheetName.MaxLength + 1)));
    }

    [Fact]
    public void Names_with_spaces_are_allowed()
    {
        var workbook = Workbook.CreateNew();

        Assert.Equal("My Sheet", workbook.AddSheet("My Sheet").Name);
    }

    [Fact]
    public void RenameSheet_enforces_uniqueness_and_rules()
    {
        var workbook = Workbook.CreateNew();
        var sheet1 = workbook.ActiveSheet;
        workbook.AddSheet("Data");

        Assert.Throws<InvalidOperationException>(() => workbook.RenameSheet(sheet1, "Data")); // duplicate
        Assert.Throws<ArgumentException>(() => workbook.RenameSheet(sheet1, "no/slash"));     // invalid

        workbook.RenameSheet(sheet1, "Summary");
        Assert.Equal("Summary", sheet1.Name);
        workbook.RenameSheet(sheet1, "Summary"); // renaming to its own name is a no-op
        Assert.Equal("Summary", sheet1.Name);
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
    public void SelectionRangeAt_returns_whole_merge_for_any_covered_cell()
    {
        var sheet = new Worksheet("S");
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:D4")));

        // The anchor and every covered cell report the full merged range.
        Assert.Equal(CellRange.Parse("B2:D4"), sheet.SelectionRangeAt(1, 1)); // anchor B2
        Assert.Equal(CellRange.Parse("B2:D4"), sheet.SelectionRangeAt(3, 3)); // covered D4
        Assert.Equal(CellRange.Parse("B2:D4"), sheet.SelectionRangeAt(2, 2)); // covered C3

        // A cell outside the merge is just itself.
        Assert.Equal(new CellRange(new CellAddress(0, 0)), sheet.SelectionRangeAt(0, 0));
    }

    [Fact]
    public void StepOverMerges_skips_past_a_merge_when_leaving_it()
    {
        var sheet = new Worksheet("S");
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:D4"))); // rows 1..3, cols 1..3

        // From inside the merge, moving down lands on the row below its bottom edge.
        Assert.Equal((4, 1), sheet.StepOverMerges(1, 1, 1, 0));
        // Moving right lands on the column past its right edge.
        Assert.Equal((1, 4), sheet.StepOverMerges(1, 1, 0, 1));
        // Moving up from inside lands above the top edge, keeping the cell's own column.
        Assert.Equal((0, 2), sheet.StepOverMerges(2, 2, -1, 0));
        // Moving left from inside lands left of the left edge, keeping the cell's own row.
        Assert.Equal((2, 0), sheet.StepOverMerges(2, 2, 0, -1));
    }

    [Fact]
    public void StepOverMerges_snaps_to_anchor_when_entering_a_merge()
    {
        var sheet = new Worksheet("S");
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:D4"))); // anchor B2 = (1,1)

        // Stepping from above into the merge's column snaps to the anchor.
        Assert.Equal((1, 1), sheet.StepOverMerges(0, 2, 1, 0));
        // Stepping from the left into a covered row snaps to the anchor.
        Assert.Equal((1, 1), sheet.StepOverMerges(3, 0, 0, 1));
    }

    [Fact]
    public void StepOverMerges_clamps_at_sheet_edges()
    {
        var sheet = new Worksheet("S");
        Assert.Equal((0, 0), sheet.StepOverMerges(0, 0, -1, -1));
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
