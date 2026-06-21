using System.IO.Compression;
using System.Text;
using Cellestra.Charts;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Serialization;

public class ChartSerializationTests
{
    private static Workbook BuildWithChart()
    {
        var workbook = Workbook.CreateNew();
        var data = workbook.ActiveSheet; // Sheet1
        data.SetValue(0, 0, CellValue.Text("Region"));
        data.SetValue(0, 1, CellValue.Text("Q1"));
        data.SetValue(0, 2, CellValue.Text("Q2"));
        for (int r = 1; r <= 3; r++)
        {
            data.SetValue(r, 0, CellValue.Text($"R{r}"));
            data.SetValue(r, 1, CellValue.Number(r * 10));
            data.SetValue(r, 2, CellValue.Number(r * 5));
        }

        var chart = new ChartDefinition
        {
            Title = "Quarterly",
            Type = ChartType.Bar, // horizontal bars → chart:bar with chart:vertical
            Stacking = ChartStacking.Percent,
            CategoriesReference = "Sheet1.A2:A4",
            ShowLegend = true,
            XAxisTitle = "Region",
            YAxisTitle = "Sales",
        };
        chart.Series.Add(new ChartSeries { NameReference = "Sheet1.B1", ValuesReference = "Sheet1.B2:B4" });
        chart.Series.Add(new ChartSeries { Name = "Second", ValuesReference = "Sheet1.C2:C4", Type = ChartType.Line });
        workbook.AddChartSheet(chart);
        return workbook;
    }

    private static (Workbook Workbook, OdsPackage Package) RoundTrip(Workbook workbook)
    {
        byte[] bytes = OdsWriter.WriteToBytes(workbook);
        using var stream = new MemoryStream(bytes);
        return OdsReader.ReadWithPackage(stream);
    }

    [Fact]
    public void Chart_sheet_round_trips_its_definition()
    {
        var (reopened, _) = RoundTrip(BuildWithChart());

        var chartSheet = reopened.FindSheet("Chart1");
        Assert.NotNull(chartSheet);
        Assert.True(chartSheet!.IsChartSheet);

        var chart = chartSheet.Chart!;
        Assert.Equal("Quarterly", chart.Title);
        Assert.Equal(ChartType.Bar, chart.Type);
        Assert.Equal(ChartStacking.Percent, chart.Stacking);
        Assert.Equal("Sheet1.A2:A4", chart.CategoriesReference);
        Assert.Equal("Region", chart.XAxisTitle);
        Assert.Equal("Sales", chart.YAxisTitle);
        Assert.True(chart.ShowLegend);

        Assert.Equal(2, chart.Series.Count);
        Assert.Equal("Sheet1.B1", chart.Series[0].NameReference);
        Assert.Equal("Sheet1.B2:B4", chart.Series[0].ValuesReference);
        Assert.Null(chart.Series[0].Type); // same as chart type → no override
        Assert.Equal("Second", chart.Series[1].Name);
        Assert.Equal(ChartType.Line, chart.Series[1].Type); // combo: per-series override preserved
    }

    [Fact]
    public void Chart_object_is_a_valid_ods_part_with_chart_class_and_frame()
    {
        byte[] bytes = OdsWriter.WriteToBytes(BuildWithChart());

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        // The embedded chart object directory exists and declares the chart class.
        var objectEntry = archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("Object", StringComparison.Ordinal) && e.FullName.EndsWith("content.xml", StringComparison.Ordinal));
        Assert.NotNull(objectEntry);
        string chartXml = ReadEntry(objectEntry!);
        Assert.Contains("chart:class=\"chart:bar\"", chartXml, StringComparison.Ordinal);
        Assert.Contains("chart:vertical=\"true\"", chartXml, StringComparison.Ordinal);   // Bar orientation
        Assert.Contains("chart:percentage=\"true\"", chartXml, StringComparison.Ordinal); // 100% stacked
        Assert.Contains("chart:class=\"chart:line\"", chartXml, StringComparison.Ordinal); // combo series

        // The spreadsheet content.xml anchors the chart with a draw frame referencing the object.
        string content = ReadEntry(archive.GetEntry("content.xml")!);
        Assert.Contains("draw:frame", content, StringComparison.Ordinal);
        Assert.Contains("draw:object", content, StringComparison.Ordinal);

        // The manifest declares the object with the chart media type.
        string manifest = ReadEntry(archive.GetEntry("META-INF/manifest.xml")!);
        Assert.Contains("application/vnd.oasis.opendocument.chart", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_sheet_without_a_chart_stays_a_plain_sheet()
    {
        var (reopened, _) = RoundTrip(BuildWithChart());
        Assert.False(reopened.FindSheet("Sheet1")!.IsChartSheet);
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
