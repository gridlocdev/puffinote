using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Data;

/// <summary>
/// A table/range header row must stay put when sorting and must never be hidden or offered as a
/// filter value (PRD §8.11–§8.12). The engines already key off <c>HasHeader</c>; these pin it.
/// </summary>
public class FilterHeaderTests
{
    private static Worksheet ScoreSheet()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Text("Score")); // header
        sheet.SetValue(1, 0, CellValue.Number(30));
        sheet.SetValue(2, 0, CellValue.Number(10));
        sheet.SetValue(3, 0, CellValue.Number(20));
        return sheet;
    }

    [Fact]
    public void Distinct_values_exclude_the_header()
    {
        var sheet = ScoreSheet();
        var values = FilterEngine.DistinctValues(sheet, CellRange.Parse("A1:A4"), 0, hasHeader: true)
            .Select(v => v.ToInvariantString())
            .ToList();

        Assert.DoesNotContain("Score", values);
        Assert.Equal(new[] { "30", "10", "20" }, values);
    }

    [Fact]
    public void Sorting_with_a_header_leaves_row_one_in_place()
    {
        var sheet = ScoreSheet();
        SortEngine.Sort(sheet, CellRange.Parse("A1:A4"),
            new SortSpec(new[] { new SortLevel(0, Ascending: true) }, HasHeader: true));

        Assert.Equal("Score", sheet.GetValue(0, 0).AsText); // header untouched
        Assert.Equal(10, sheet.GetValue(1, 0).AsNumber);    // data sorted below it
        Assert.Equal(30, sheet.GetValue(3, 0).AsNumber);
    }

    [Fact]
    public void Filtering_with_a_header_never_hides_row_one()
    {
        var sheet = ScoreSheet();
        var spec = new FilterSpec(
            new Dictionary<int, ColumnFilter> { [0] = new ColumnFilter { Condition = ">15" } },
            HasHeader: true);

        FilterEngine.Apply(sheet, CellRange.Parse("A1:A4"), spec, CultureInfo.InvariantCulture);

        Assert.False(sheet.IsRowHidden(0)); // header always visible
        Assert.True(sheet.IsRowHidden(2));  // value 10 hidden
    }
}
