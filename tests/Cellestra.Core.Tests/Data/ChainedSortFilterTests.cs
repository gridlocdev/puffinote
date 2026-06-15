using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Data;

/// <summary>
/// Chaining sort and filter (the Sort &amp; Filter dialog): context-aware value lists so a second
/// filter only offers values that survive the first, a non-mutating row count for the dialog's
/// preview, and a single-undo composite that restores both order and visibility together.
/// </summary>
public class ChainedSortFilterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Name | Status
    //  a   | Open
    //  b   | Done
    //  c   | Open
    //  d   | Done
    private static Worksheet TaskSheet()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Status"));
        (string Name, string Status)[] rows = { ("a", "Open"), ("b", "Done"), ("c", "Open"), ("d", "Done") };
        for (int i = 0; i < rows.Length; i++)
        {
            sheet.SetValue(i + 1, 0, CellValue.Text(rows[i].Name));
            sheet.SetValue(i + 1, 1, CellValue.Text(rows[i].Status));
        }

        return sheet;
    }

    private static FilterSpec StatusFilter(params string[] allowed) =>
        new(new Dictionary<int, ColumnFilter> { [1] = new ColumnFilter { AllowedValues = allowed.ToHashSet() } });

    [Fact]
    public void Distinct_values_narrow_to_rows_surviving_the_other_filter()
    {
        var sheet = TaskSheet();
        var range = CellRange.Parse("A1:B5");

        // With Status filtered to "Open", the Name column should only offer a and c.
        var names = FilterEngine.DistinctValues(sheet, range, column: 0, hasHeader: true, StatusFilter("Open"), Inv)
            .Select(v => v.AsText)
            .ToList();

        Assert.Equal(new[] { "a", "c" }, names);
    }

    [Fact]
    public void A_columns_own_filter_does_not_narrow_its_own_value_list()
    {
        var sheet = TaskSheet();
        var range = CellRange.Parse("A1:B5");

        // Status filtered to "Open", but Status's own dropdown must still show Open AND Done so the
        // user can widen the selection (mirrors Excel).
        var statuses = FilterEngine.DistinctValues(sheet, range, column: 1, hasHeader: true, StatusFilter("Open"), Inv)
            .Select(v => v.AsText)
            .ToList();

        Assert.Equal(new[] { "Open", "Done" }, statuses);
    }

    [Fact]
    public void Plain_distinct_overload_is_unfiltered()
    {
        var sheet = TaskSheet();
        var names = FilterEngine.DistinctValues(sheet, CellRange.Parse("A1:B5"), 0, hasHeader: true)
            .Select(v => v.AsText);
        Assert.Equal(new[] { "a", "b", "c", "d" }, names);
    }

    [Fact]
    public void CountVisible_counts_without_hiding_any_row()
    {
        var sheet = TaskSheet();
        var range = CellRange.Parse("A1:B5");

        int visible = FilterEngine.CountVisible(sheet, range, StatusFilter("Open"), Inv);

        Assert.Equal(2, visible);
        Assert.False(sheet.IsRowHidden(1)); // preview must not mutate the sheet
        Assert.False(sheet.IsRowHidden(2));
    }

    [Fact]
    public void ApplySortAndFilter_sorts_and_filters_then_undoes_as_one_step()
    {
        var workbook = Workbook.CreateNew();
        var history = new EditHistory(workbook);
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("N")); sheet.SetValue(0, 1, CellValue.Text("S"));
        sheet.SetValue(1, 0, CellValue.Number(3)); sheet.SetValue(1, 1, CellValue.Text("Open"));
        sheet.SetValue(2, 0, CellValue.Number(1)); sheet.SetValue(2, 1, CellValue.Text("Done"));
        sheet.SetValue(3, 0, CellValue.Number(2)); sheet.SetValue(3, 1, CellValue.Text("Open"));

        var range = CellRange.Parse("A1:B4");
        var sort = new SortSpec(new[] { new SortLevel(0, Ascending: true) });
        var filter = StatusFilter("Open");

        // The handler sets these before executing so the sort's ReapplyActiveFilter sees the new filter.
        sheet.ActiveFilter = range;
        sheet.ActiveFilterSpec = filter;
        sheet.ActiveSort = sort;

        history.Execute(DataEditCommands.ApplySortAndFilter(sheet.Name, range, sort, filter, Inv));

        // Sorted ascending by column 0, then "Done" hidden.
        Assert.Equal(1, sheet.GetValue(1, 0).AsNumber);
        Assert.Equal(2, sheet.GetValue(2, 0).AsNumber);
        Assert.Equal(3, sheet.GetValue(3, 0).AsNumber);
        Assert.True(sheet.IsRowHidden(1));   // (1, Done) hidden
        Assert.False(sheet.IsRowHidden(2));  // (2, Open) visible

        history.Undo();

        Assert.Equal(3, sheet.GetValue(1, 0).AsNumber); // original order restored
        Assert.Equal(1, sheet.GetValue(2, 0).AsNumber);
        Assert.False(sheet.IsRowHidden(1)); // original visibility restored
        Assert.False(sheet.IsRowHidden(2));
    }

    [Fact]
    public void ApplyTableSortAndFilter_stores_state_on_the_table_and_undoes_as_one_step()
    {
        var workbook = Workbook.CreateNew();
        var history = new EditHistory(workbook);
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("N")); sheet.SetValue(0, 1, CellValue.Text("S"));
        sheet.SetValue(1, 0, CellValue.Number(3)); sheet.SetValue(1, 1, CellValue.Text("Open"));
        sheet.SetValue(2, 0, CellValue.Number(1)); sheet.SetValue(2, 1, CellValue.Text("Done"));
        sheet.SetValue(3, 0, CellValue.Number(2)); sheet.SetValue(3, 1, CellValue.Text("Open"));
        var range = CellRange.Parse("A1:B4");
        var table = new Table("T", range, HasHeader: true);
        sheet.AddTable(table);

        var sort = new SortSpec(new[] { new SortLevel(0, Ascending: true) });
        var filter = StatusFilter("Open");
        history.Execute(DataEditCommands.ApplyTableSortAndFilter(sheet.Name, table, sort, filter, Inv));

        // The filter/sort live on the table record (single source of truth), not the sheet AutoFilter.
        var stored = Assert.Single(sheet.Tables);
        Assert.NotNull(stored.Filter);
        Assert.NotNull(stored.Sort);
        Assert.Null(sheet.ActiveFilterSpec);
        Assert.Equal(1, sheet.GetValue(1, 0).AsNumber); // sorted ascending
        Assert.True(sheet.IsRowHidden(1));              // (1, Done) hidden
        Assert.False(sheet.IsRowHidden(2));             // (2, Open) visible

        history.Undo();

        Assert.Null(Assert.Single(sheet.Tables).Filter); // table record restored
        Assert.Null(sheet.Tables[0].Sort);
        Assert.Equal(3, sheet.GetValue(1, 0).AsNumber);  // original order restored
        Assert.False(sheet.IsRowHidden(1));              // original visibility restored
    }

    [Fact]
    public void ApplySortAndFilter_with_no_filter_unhides_previously_hidden_rows()
    {
        var workbook = Workbook.CreateNew();
        var history = new EditHistory(workbook);
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("S"));
        sheet.SetValue(1, 0, CellValue.Text("Open"));
        sheet.SetValue(2, 0, CellValue.Text("Done"));
        var range = CellRange.Parse("A1:A3");

        // Start with a row hidden by a filter.
        FilterEngine.Apply(sheet, range, new FilterSpec(
            new Dictionary<int, ColumnFilter> { [0] = new ColumnFilter { AllowedValues = new HashSet<string> { "Open" } } }), Inv);
        Assert.True(sheet.IsRowHidden(2));

        sheet.ActiveFilter = null;
        sheet.ActiveFilterSpec = null;
        history.Execute(DataEditCommands.ApplySortAndFilter(sheet.Name, range, sort: null, filter: null, Inv));

        Assert.False(sheet.IsRowHidden(2)); // clearing the filter shows it again
    }
}
