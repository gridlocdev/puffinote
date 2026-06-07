using System.IO.Compression;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Serialization;

public class OdsRoundTripTests
{
    private static Workbook BuildSample()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Number(42));
        sheet.SetValue(0, 1, CellValue.Text("hi"));
        sheet.SetValue(0, 2, CellValue.Boolean(true));
        sheet.SetFormula(1, 0, "A1*2", CellValue.Number(84));

        int styleId = workbook.Styles.Intern(CellStyle.Default
            .WithFont(Font.Default.WithBold(true))
            .WithFill(Fill.Solid(ArgbColor.FromRgb(255, 0, 0)))
            .WithNumberFormat(new NumberFormat("0.00")));
        sheet.SetValue(0, 3, CellValue.Number(3.14159));
        sheet.GetOrCreate(0, 3).StyleId = styleId;

        sheet.GetOrCreateColumn(0).Width = 100;
        sheet.GetOrCreateRow(0).Height = 30;
        sheet.GetOrCreateColumn(2).Hidden = true;
        sheet.GetOrCreateRow(3).Hidden = true;

        sheet.AddMerge(new MergedRegion(CellRange.Parse("A5:B6")));
        workbook.AddNamedRange(new NamedRange("Rate", CellRange.Parse("Sheet1.A1")));

        var sheet2 = workbook.AddSheet();
        sheet2.SetValue(0, 0, CellValue.Number(7));

        workbook.Settings.ActiveSheetIndex = 1;
        workbook.Settings.CalculationMode = CalculationMode.Manual;
        return workbook;
    }

    private static Workbook RoundTrip(Workbook workbook)
    {
        byte[] bytes = OdsWriter.WriteToBytes(workbook);
        using var stream = new MemoryStream(bytes);
        return OdsReader.Read(stream);
    }

    [Fact]
    public void Values_and_formula_survive()
    {
        var sheet = RoundTrip(BuildSample()).FindSheet("Sheet1")!;

        Assert.Equal(42, sheet.GetValue(0, 0).AsNumber);
        Assert.Equal("hi", sheet.GetValue(0, 1).AsText);
        Assert.True(sheet.GetValue(0, 2).AsBoolean);

        var formulaCell = sheet.Find(1, 0)!;
        Assert.Equal("A1*2", formulaCell.Formula);
        Assert.Equal(84, formulaCell.Value.AsNumber); // cached value loaded for instant display
    }

    [Fact]
    public void Styles_survive()
    {
        var result = RoundTrip(BuildSample());
        var sheet = result.FindSheet("Sheet1")!;
        var style = result.Styles.Get(sheet.Find(0, 3)!.StyleId);

        Assert.True(style.Font.Bold);
        Assert.True(style.Fill.HasBackground);
        Assert.Equal(ArgbColor.FromRgb(255, 0, 0), style.Fill.Background);
        Assert.Equal("0.00", style.NumberFormat.Code);
    }

    [Fact]
    public void Layout_survives()
    {
        var sheet = RoundTrip(BuildSample()).FindSheet("Sheet1")!;

        Assert.Equal(100, sheet.GetColumnWidth(0), 0.5);
        Assert.Equal(30, sheet.GetRowHeight(0), 0.5);
        Assert.True(sheet.IsColumnHidden(2));
        Assert.True(sheet.IsRowHidden(3));
    }

    [Fact]
    public void Merges_survive()
    {
        var sheet = RoundTrip(BuildSample()).FindSheet("Sheet1")!;
        var merge = sheet.FindMerge(4, 0);

        Assert.NotNull(merge);
        Assert.Equal(CellRange.Parse("A5:B6"), merge!.Range);
    }

    [Fact]
    public void Sheets_named_ranges_and_settings_survive()
    {
        var result = RoundTrip(BuildSample());

        Assert.Equal(2, result.SheetCount);
        Assert.Equal(7, result.FindSheet("Sheet2")!.GetValue(0, 0).AsNumber);
        Assert.NotNull(result.FindNamedRange("Rate"));
        Assert.Equal(1, result.Settings.ActiveSheetIndex);
        Assert.Equal(CalculationMode.Manual, result.Settings.CalculationMode);
    }

    [Fact]
    public void Mimetype_is_first_and_stored_uncompressed()
    {
        byte[] bytes = OdsWriter.WriteToBytes(BuildSample());
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var first = archive.Entries[0];
        Assert.Equal("mimetype", first.FullName);
        Assert.Equal(first.Length, first.CompressedLength); // stored, not deflated
    }
}
