using System.IO.Compression;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Serialization;

/// <summary>
/// Number formats through open → save → reopen. Currency is the one that used to lose something real:
/// ODF carries the symbol only in <c>number:currency-style</c>'s <c>number:currency-symbol</c>, so a
/// currency written as a plain <c>number:number-style</c> came back as a thousands-separated number.
/// </summary>
public class OdsNumberFormatTests
{
    private static Workbook WithFormat(string code)
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1234.5));
        sheet.GetOrCreate(0, 0).StyleId = workbook.Styles.Intern(
            CellStyle.Default.WithNumberFormat(new NumberFormat(code)));
        return workbook;
    }

    private static NumberFormat RoundTrip(string code)
    {
        byte[] bytes = OdsWriter.WriteToBytes(WithFormat(code));
        using var stream = new MemoryStream(bytes);
        var reopened = OdsReader.Read(stream);
        var sheet = reopened.FindSheet("Sheet1")!;
        return reopened.Styles.Get(sheet.Find(0, 0)!.StyleId).NumberFormat;
    }

    private static string ContentXml(string code)
    {
        byte[] bytes = OdsWriter.WriteToBytes(WithFormat(code));
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("content.xml")!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void Currency_survives_as_currency()
    {
        // The bug: this reopened as "#,##0.00" — still a format, no longer money.
        var format = RoundTrip("$#,##0.00");

        Assert.Equal("$#,##0.00", format.Code);
        Assert.Equal(NumberFormatKind.Currency, format.Kind);
    }

    [Fact]
    public void Currency_is_written_as_an_ODF_currency_style()
    {
        string xml = ContentXml("$#,##0.00");

        Assert.Contains("currency-style", xml, StringComparison.Ordinal);
        Assert.Contains("currency-symbol", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailing_currency_symbol_stays_trailing()
    {
        var format = RoundTrip("#,##0.00 €");

        Assert.Equal("#,##0.00 €", format.Code);
        Assert.Equal(NumberFormatKind.Currency, format.Kind);
    }

    [Fact]
    public void Currency_without_grouping_survives()
    {
        var format = RoundTrip("£0.00");

        Assert.Equal("£0.00", format.Code);
        Assert.Equal(NumberFormatKind.Currency, format.Kind);
    }

    [Theory]
    [InlineData("0.00", NumberFormatKind.Number)]
    [InlineData("#,##0.00", NumberFormatKind.Number)]
    [InlineData("0.00%", NumberFormatKind.Percent)]
    [InlineData("yyyy-mm-dd", NumberFormatKind.Date)]
    public void The_other_formats_still_survive(string code, NumberFormatKind kind)
    {
        var format = RoundTrip(code);

        Assert.Equal(code, format.Code);
        Assert.Equal(kind, format.Kind);
    }
}
