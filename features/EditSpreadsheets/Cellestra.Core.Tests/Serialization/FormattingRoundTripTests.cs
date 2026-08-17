using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Serialization;

/// <summary>
/// A sweep of "does what I formatted come back the way I left it" — every property a
/// <see cref="CellStyle"/> carries, plus the row/column default styles, through open → save → reopen.
/// <para>
/// This exists because the losses it covers were all silent: nothing failed, nothing warned, the
/// document simply said something slightly different the next time it was opened. A format the file
/// chose and Cellestra rewrites is a document edit nobody asked for, so the sweep is broad on purpose
/// — including codes Cellestra's own UI can't produce, which only another editor can author and which
/// therefore have no other guard.
/// </para>
/// </summary>
public class FormattingRoundTripTests
{
    private static Workbook RoundTrip(Workbook workbook)
    {
        byte[] bytes = OdsWriter.WriteToBytes(workbook);
        using var stream = new MemoryStream(bytes);
        return OdsReader.Read(stream);
    }

    private static CellStyle Survive(CellStyle style, bool withValue = true)
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        if (withValue)
        {
            sheet.SetValue(0, 0, CellValue.Number(1234.5));
        }

        sheet.GetOrCreate(0, 0).StyleId = workbook.Styles.Intern(style);

        var reopened = RoundTrip(workbook);
        var cell = reopened.FindSheet("Sheet1")!.Find(0, 0);
        return cell is null ? CellStyle.Default : reopened.Styles.Get(cell.StyleId);
    }

    private static NumberFormat SurviveFormat(string code) =>
        Survive(CellStyle.Default.WithNumberFormat(new NumberFormat(code))).NumberFormat;

    [Theory]
    [InlineData("General", NumberFormatKind.General)]
    [InlineData("0", NumberFormatKind.Number)]
    [InlineData("0.00", NumberFormatKind.Number)]
    [InlineData("#,##0", NumberFormatKind.Number)]
    [InlineData("#,##0.00", NumberFormatKind.Number)]
    [InlineData("0%", NumberFormatKind.Percent)]
    [InlineData("0.00%", NumberFormatKind.Percent)]
    [InlineData("#,##0.00%", NumberFormatKind.Percent)]          // grouping used to be dropped
    [InlineData("$#,##0.00", NumberFormatKind.Currency)]          // the symbol used to be dropped
    [InlineData("#,##0.00 €", NumberFormatKind.Currency)]
    [InlineData("yyyy-mm-dd", NumberFormatKind.Date)]
    [InlineData("dd/mm/yyyy", NumberFormatKind.Date)]
    [InlineData("mmm yyyy", NumberFormatKind.Date)]               // a textual month used to come back numeric
    [InlineData("mmmm d, yyyy", NumberFormatKind.Date)]
    [InlineData("hh:mm:ss", NumberFormatKind.Time)]
    [InlineData("0.00E+00", NumberFormatKind.Scientific)]         // used to come back as 0.00
    [InlineData("# ?/?", NumberFormatKind.Fraction)]              // used to come back as General
    [InlineData("@", NumberFormatKind.Text)]                      // used to come back as General
    public void A_number_format_survives_with_its_kind(string code, NumberFormatKind kind)
    {
        var format = SurviveFormat(code);

        Assert.Equal(code, format.Code);
        Assert.Equal(kind, format.Kind);
    }

    [Fact]
    public void A_multi_section_code_keeps_its_first_section_and_is_not_read_as_a_date()
    {
        // Known limitation, asserted so it stays visible: Cellestra models one section per format, so
        // a code with negative/zero/text sections keeps only the first. What it must NOT do is what it
        // used to — read the "D" of "[RED]" as a date field, which made the *number* render as a date.
        Assert.Equal(NumberFormatKind.Number, new NumberFormat("0.0;[RED]-0.0").Kind);
        Assert.Equal("0.0", SurviveFormat("0.0;[RED]-0.0").Code);
    }

    [Theory]
    [InlineData("0\" days\"", NumberFormatKind.Number)]   // the "d" is text the format prints, not a date field
    [InlineData("[h]:mm", NumberFormatKind.Time)]         // …but [h] is the elapsed-hours token, and is a field
    [InlineData("[$€-407]#,##0.00", NumberFormatKind.Currency)]
    public void A_literal_does_not_decide_the_kind(string code, NumberFormatKind kind) =>
        Assert.Equal(kind, new NumberFormat(code).Kind);

    [Fact]
    public void Font_properties_survive()
    {
        var style = CellStyle.Default.WithFont(new Font("Georgia", 14.5, Bold: true, Italic: true,
            Underline: true, Strikethrough: true, Color: ArgbColor.FromRgb(10, 20, 30)));

        Assert.Equal(style.Font, Survive(style).Font);
    }

    [Theory]
    [InlineData(HorizontalAlignment.General)]
    [InlineData(HorizontalAlignment.Left)]
    [InlineData(HorizontalAlignment.Center)]
    [InlineData(HorizontalAlignment.Right)]
    public void Horizontal_alignment_survives(HorizontalAlignment horizontal) =>
        Assert.Equal(horizontal, Survive(CellStyle.Default.WithAlignment(new Alignment(horizontal))).Alignment.Horizontal);

    [Theory]
    [InlineData(VerticalAlignment.Top)]
    [InlineData(VerticalAlignment.Middle)]
    [InlineData(VerticalAlignment.Bottom)]
    public void Vertical_alignment_survives(VerticalAlignment vertical) =>
        Assert.Equal(vertical, Survive(CellStyle.Default.WithAlignment(new Alignment(Vertical: vertical))).Alignment.Vertical);

    [Fact]
    public void Wrap_text_survives()
    {
        var style = CellStyle.Default.WithAlignment(new Alignment(WrapText: true));
        Assert.True(Survive(style).Alignment.WrapText);
    }

    [Fact]
    public void Fill_survives()
    {
        var style = CellStyle.Default.WithFill(Fill.Solid(ArgbColor.FromRgb(1, 2, 3)));
        Assert.Equal(style.Fill, Survive(style).Fill);
    }

    [Theory]
    [InlineData(BorderLineStyle.Thin)]
    [InlineData(BorderLineStyle.Medium)]
    [InlineData(BorderLineStyle.Thick)]
    [InlineData(BorderLineStyle.Dashed)]
    [InlineData(BorderLineStyle.Dotted)]
    [InlineData(BorderLineStyle.Double)]
    public void Border_style_survives(BorderLineStyle line)
    {
        var style = CellStyle.Default.WithBorder(Border.All(line));
        Assert.Equal(style.Border, Survive(style).Border);
    }

    [Fact]
    public void A_styled_but_empty_cell_survives()
    {
        var style = CellStyle.Default
            .WithFill(Fill.Solid(ArgbColor.FromRgb(200, 0, 0)))
            .WithFont(Font.Default.WithBold(true));
        var after = Survive(style, withValue: false);

        Assert.Equal(style.Fill, after.Fill);
        Assert.True(after.Font.Bold);
    }

    [Fact]
    public void A_column_default_style_survives()
    {
        var workbook = Workbook.CreateNew();
        int styleId = workbook.Styles.Intern(CellStyle.Default.WithFont(Font.Default.WithBold(true)));
        workbook.ActiveSheet.GetOrCreateColumn(1).StyleId = styleId;

        var sheet = RoundTrip(workbook).FindSheet("Sheet1")!;
        Assert.True(RoundTrip(workbook).Styles.Get(sheet.FindColumn(1)!.StyleId).Font.Bold);
    }

    [Fact]
    public void A_row_default_style_survives()
    {
        // A row's default cell style was written nowhere and read nowhere, though the model resolves
        // through it (cell → row → column → default) — so a whole-row format authored in another
        // editor was dropped the first time Cellestra saved the file.
        var workbook = Workbook.CreateNew();
        int styleId = workbook.Styles.Intern(CellStyle.Default.WithFont(Font.Default.WithItalic(true)));
        workbook.ActiveSheet.GetOrCreateRow(2).StyleId = styleId;

        var reopened = RoundTrip(workbook);
        var sheet = reopened.FindSheet("Sheet1")!;
        Assert.True(reopened.Styles.Get(sheet.FindRow(2)!.StyleId).Font.Italic);
        Assert.True(reopened.Styles.Get(sheet.EffectiveStyleId(2, 5)).Font.Italic);
    }
}
