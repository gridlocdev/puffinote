using Cellestra.Charts;
using Cellestra.Core.Charts;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Charts;

public class ChartDataResolverTests
{
    private static Workbook BuildData()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet; // Sheet1
        sheet.SetValue(0, 1, CellValue.Text("Sales"));    // B1 header / series name
        sheet.SetValue(0, 0, CellValue.Text("Category")); // A1
        string[] labels = ["Jan", "Feb", "Mar"];
        double[] values = [5, 8, 3];
        for (int i = 0; i < labels.Length; i++)
        {
            sheet.SetValue(i + 1, 0, CellValue.Text(labels[i]));
            sheet.SetValue(i + 1, 1, CellValue.Number(values[i]));
        }

        return workbook;
    }

    [Fact]
    public void Resolves_categories_values_and_series_name_from_a_cell()
    {
        var workbook = BuildData();
        var chart = new ChartDefinition
        {
            CategoriesReference = "Sheet1.A2:A4",
        };
        chart.Series.Add(new ChartSeries { NameReference = "Sheet1.B1", ValuesReference = "Sheet1.B2:B4" });

        var resolved = ChartDataResolver.Resolve(workbook, chart);

        Assert.Equal(["Jan", "Feb", "Mar"], resolved.Categories);
        Assert.Single(resolved.Series);
        Assert.Equal("Sales", resolved.Series[0].Name);
        Assert.Equal([5d, 8d, 3d], resolved.Series[0].Values);
        Assert.False(resolved.IsEmpty);
    }

    [Fact]
    public void Unqualified_reference_defaults_to_the_first_data_sheet()
    {
        var workbook = BuildData();
        var chart = new ChartDefinition();
        chart.Series.Add(new ChartSeries { ValuesReference = "B2:B4" }); // no sheet qualifier

        var resolved = ChartDataResolver.Resolve(workbook, chart);

        Assert.Equal([5d, 8d, 3d], resolved.Series[0].Values);
        Assert.Equal("Series 1", resolved.Series[0].Name); // no name reference / literal → fallback
    }

    [Fact]
    public void Blank_or_text_cells_become_nan_gaps()
    {
        var workbook = BuildData();
        workbook.ActiveSheet.SetValue(2, 1, CellValue.Empty); // blank out B3
        var chart = new ChartDefinition();
        chart.Series.Add(new ChartSeries { ValuesReference = "Sheet1.B2:B4" });

        var resolved = ChartDataResolver.Resolve(workbook, chart);

        Assert.Equal(5d, resolved.Series[0].Values[0]);
        Assert.True(double.IsNaN(resolved.Series[0].Values[1]));
        Assert.Equal(3d, resolved.Series[0].Values[2]);
    }

    [Fact]
    public void A_chart_with_no_series_is_empty()
    {
        var resolved = ChartDataResolver.Resolve(BuildData(), new ChartDefinition());
        Assert.True(resolved.IsEmpty);
    }
}
