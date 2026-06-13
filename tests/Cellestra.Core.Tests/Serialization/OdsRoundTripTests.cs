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
            .WithFont(Font.Default.WithBold(true).WithStrikethrough(true))
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
    public void Date_time_of_day_survives_round_trip()
    {
        // A datetime cell (2026-06-13 15:30) carries a fractional serial; saving must not
        // floor it to midnight (that silently destroyed every time-of-day on save).
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        double serial = Cellestra.Core.Formulas.Evaluation.DateConversion.ToSerial(new DateTime(2026, 6, 13, 15, 30, 0));

        int dateTimeStyle = workbook.Styles.Intern(
            CellStyle.Default.WithNumberFormat(new NumberFormat("YYYY-MM-DD HH:MM:SS")));
        sheet.SetValue(0, 0, CellValue.Number(serial));
        sheet.GetOrCreate(0, 0).StyleId = dateTimeStyle;

        // A pure time-of-day cell (noon = 0.5) and an elapsed time past 24h must both survive.
        int timeStyle = workbook.Styles.Intern(CellStyle.Default.WithNumberFormat(new NumberFormat("HH:MM:SS")));
        sheet.SetValue(1, 0, CellValue.Number(0.5));
        sheet.GetOrCreate(1, 0).StyleId = timeStyle;
        sheet.SetValue(2, 0, CellValue.Number(1.25));
        sheet.GetOrCreate(2, 0).StyleId = timeStyle;

        var result = RoundTrip(workbook).FindSheet("Sheet1")!;

        Assert.Equal(serial, result.GetValue(0, 0).AsNumber, 6);
        Assert.Equal(0.5, result.GetValue(1, 0).AsNumber, 6);
        Assert.Equal(1.25, result.GetValue(2, 0).AsNumber, 6);
    }

    [Fact]
    public void Styles_survive()
    {
        var result = RoundTrip(BuildSample());
        var sheet = result.FindSheet("Sheet1")!;
        var style = result.Styles.Get(sheet.Find(0, 3)!.StyleId);

        Assert.True(style.Font.Bold);
        Assert.True(style.Font.Strikethrough);
        Assert.True(style.Fill.HasBackground);
        Assert.Equal(ArgbColor.FromRgb(255, 0, 0), style.Fill.Background);
        Assert.Equal("0.00", style.NumberFormat.Code);
    }

    [Fact]
    public void Borders_survive()
    {
        // A workbook with mixed per-edge border styles and a non-black colour must survive
        // open → save → reopen (the UI sets these via Format Cells / the Border drawer).
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        var border = new Border(
            Left: new BorderEdge(BorderLineStyle.Dashed, ArgbColor.FromRgb(0, 0, 255)),
            Right: BorderEdge.Of(BorderLineStyle.Thick),
            Top: BorderEdge.Of(BorderLineStyle.Thin),
            Bottom: new BorderEdge(BorderLineStyle.Double, ArgbColor.FromRgb(0, 128, 0)));
        int styleId = workbook.Styles.Intern(CellStyle.Default.WithBorder(border));
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.GetOrCreate(0, 0).StyleId = styleId;

        var result = RoundTrip(workbook);
        var reopened = result.FindSheet("Sheet1")!;
        var b = result.Styles.Get(reopened.Find(0, 0)!.StyleId).Border;

        Assert.Equal(BorderLineStyle.Dashed, b.Left.Style);
        Assert.Equal(ArgbColor.FromRgb(0, 0, 255), b.Left.Color);
        Assert.Equal(BorderLineStyle.Thick, b.Right.Style);
        Assert.Equal(BorderLineStyle.Thin, b.Top.Style);
        Assert.Equal(BorderLineStyle.Double, b.Bottom.Style);
        Assert.Equal(ArgbColor.FromRgb(0, 128, 0), b.Bottom.Color);
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
