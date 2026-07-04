using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Serialization;

/// <summary>
/// Round-trips the ODF <c>table:database-range</c> that carries a worksheet's filter region,
/// filter conditions, and sort order (tasks 17b/18). A filter+sort defined on open must survive
/// save → reopen, and the rows the filter hides must come back hidden because the filter is
/// reapplied on open (PRD §8.12).
/// </summary>
public class OdsDatabaseRangeTests
{
    private static Workbook RoundTrip(Workbook workbook)
    {
        byte[] bytes = OdsWriter.WriteToBytes(workbook);
        using var stream = new MemoryStream(bytes);
        return OdsReader.Read(stream);
    }

    private static Workbook BuildWithSheetFilter()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;

        // Header + four data rows.
        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Qty"));
        sheet.SetValue(1, 0, CellValue.Text("apple"));
        sheet.SetValue(1, 1, CellValue.Number(3));
        sheet.SetValue(2, 0, CellValue.Text("banana"));
        sheet.SetValue(2, 1, CellValue.Number(8));
        sheet.SetValue(3, 0, CellValue.Text("cherry"));
        sheet.SetValue(3, 1, CellValue.Number(1));
        sheet.SetValue(4, 0, CellValue.Text("apple"));
        sheet.SetValue(4, 1, CellValue.Number(9));

        var range = CellRange.Parse("A1:B5");

        // Column 0: a value checklist (apple only); Column 1: a relational condition (> 2).
        var spec = new FilterSpec(new Dictionary<int, ColumnFilter>
        {
            [0] = new ColumnFilter { AllowedValues = new HashSet<string> { "apple" } },
            [1] = new ColumnFilter { Condition = ">2" },
        });

        var sort = new SortSpec([new SortLevel(1, Ascending: false)]);

        FilterEngine.Apply(sheet, range, spec, CultureInfo.InvariantCulture);
        sheet.ActiveFilter = range;
        sheet.ActiveFilterSpec = spec;
        sheet.ActiveSort = sort;
        return workbook;
    }

    [Fact]
    public void Sheet_filter_region_conditions_and_sort_survive()
    {
        var sheet = RoundTrip(BuildWithSheetFilter()).FindSheet("Sheet1")!;

        Assert.Equal(CellRange.Parse("A1:B5"), sheet.ActiveFilter);

        var spec = sheet.ActiveFilterSpec;
        Assert.NotNull(spec);
        Assert.True(spec!.HasHeader);
        Assert.Equal(2, spec.Columns.Count);
        Assert.Equal(["apple"], spec.Columns[0].AllowedValues!.OrderBy(v => v));
        Assert.Equal(">2", spec.Columns[1].Condition);

        var sort = sheet.ActiveSort;
        Assert.NotNull(sort);
        var level = Assert.Single(sort!.Levels);
        Assert.Equal(1, level.Column);
        Assert.False(level.Ascending);
    }

    [Fact]
    public void Rows_hidden_by_the_filter_stay_hidden_on_open()
    {
        var sheet = RoundTrip(BuildWithSheetFilter()).FindSheet("Sheet1")!;

        // apple AND qty>2 → rows 1 (apple,3) and 4 (apple,9) visible; 2 (banana) and 3 (cherry) hidden.
        Assert.False(sheet.IsRowHidden(1));
        Assert.True(sheet.IsRowHidden(2));
        Assert.True(sheet.IsRowHidden(3));
        Assert.False(sheet.IsRowHidden(4));
    }

    [Fact]
    public void Table_filter_and_sort_survive()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("City"));
        sheet.SetValue(1, 0, CellValue.Text("Oslo"));
        sheet.SetValue(2, 0, CellValue.Text("Lima"));

        var range = CellRange.Parse("A1:A3");
        var filter = new FilterSpec(new Dictionary<int, ColumnFilter>
        {
            [0] = new ColumnFilter { AllowedValues = new HashSet<string> { "Oslo", "Lima" } },
        });
        var sort = new SortSpec([new SortLevel(0)]);
        sheet.AddTable(new Table("Cities", range, HasHeader: true) { Filter = filter, Sort = sort });

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        var table = Assert.Single(reopened.Tables);
        Assert.Equal("Cities", table.Name);
        Assert.Equal(range, table.Range);
        Assert.NotNull(table.Filter);
        Assert.Equal(2, table.Filter!.Columns[0].AllowedValues!.Count);
        Assert.NotNull(table.Sort);
        Assert.Equal(0, Assert.Single(table.Sort!.Levels).Column);
    }

    [Fact]
    public void Multi_value_checklist_round_trips_via_filter_or()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Color"));
        sheet.SetValue(1, 0, CellValue.Text("red"));
        sheet.SetValue(2, 0, CellValue.Text("green"));
        sheet.SetValue(3, 0, CellValue.Text("blue"));

        var range = CellRange.Parse("A1:A4");
        var spec = new FilterSpec(new Dictionary<int, ColumnFilter>
        {
            [0] = new ColumnFilter { AllowedValues = new HashSet<string> { "red", "blue" } },
        });
        FilterEngine.Apply(sheet, range, spec, CultureInfo.InvariantCulture);
        sheet.ActiveFilter = range;
        sheet.ActiveFilterSpec = spec;

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        Assert.Equal(
            ["blue", "red"],
            reopened.ActiveFilterSpec!.Columns[0].AllowedValues!.OrderBy(v => v));
    }

    [Fact]
    public void Multi_level_sort_and_multi_column_filter_survive()
    {
        // The chained pipeline a Sort & Filter dialog produces: sort by two columns, filter on two.
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Region"));
        sheet.SetValue(0, 1, CellValue.Text("Status"));
        sheet.SetValue(1, 0, CellValue.Text("West"));
        sheet.SetValue(1, 1, CellValue.Text("Open"));
        sheet.SetValue(2, 0, CellValue.Text("East"));
        sheet.SetValue(2, 1, CellValue.Text("Done"));

        var range = CellRange.Parse("A1:B3");
        sheet.ActiveFilter = range;
        sheet.ActiveFilterSpec = new FilterSpec(new Dictionary<int, ColumnFilter>
        {
            [0] = new ColumnFilter { AllowedValues = new HashSet<string> { "West", "East" } },
            [1] = new ColumnFilter { Condition = "Open" },
        });
        sheet.ActiveSort = new SortSpec([new SortLevel(0, Ascending: true), new SortLevel(1, Ascending: false)]);

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;

        var sort = reopened.ActiveSort;
        Assert.NotNull(sort);
        Assert.Equal([0, 1], sort!.Levels.Select(l => l.Column)); // both levels, in priority order
        Assert.True(sort.Levels[0].Ascending);
        Assert.False(sort.Levels[1].Ascending);
        Assert.Equal(2, reopened.ActiveFilterSpec!.Columns.Count);
    }

    [Fact]
    public void No_filter_emits_no_database_range()
    {
        var workbook = Workbook.CreateNew();
        workbook.ActiveSheet.SetValue(0, 0, CellValue.Number(1));

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        Assert.Null(reopened.ActiveFilter);
        Assert.Null(reopened.ActiveFilterSpec);
        Assert.Empty(reopened.Tables);
    }
}
