using System.Globalization;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Styling;

public class StyleTests
{
    [Fact]
    public void StylePool_dedupes_equal_styles()
    {
        var pool = new StylePool();
        var bold = CellStyle.Default.WithFont(Font.Default.WithBold(true));

        int first = pool.Intern(bold);
        int second = pool.Intern(CellStyle.Default.WithFont(Font.Default.WithBold(true)));

        Assert.Equal(first, second);
        Assert.Equal(2, pool.Count); // default + bold
    }

    [Fact]
    public void StylePool_default_is_index_zero()
    {
        var pool = new StylePool();
        Assert.Equal(CellStyle.Default, pool.Get(StylePool.DefaultId));
        Assert.Equal(0, pool.Intern(CellStyle.Default));
    }

    [Fact]
    public void StylePool_mutate_derives_and_interns()
    {
        var pool = new StylePool();
        int id = pool.Mutate(StylePool.DefaultId, s => s.WithFill(Fill.Solid(ArgbColor.FromRgb(255, 0, 0))));

        Assert.True(pool.Get(id).Fill.HasBackground);
        Assert.Equal(ArgbColor.FromRgb(255, 0, 0), pool.Get(id).Fill.Background);
    }

    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("00FF00", 0, 255, 0)]
    public void ArgbColor_parses_hex(string hex, byte r, byte g, byte b)
    {
        Assert.True(ArgbColor.TryFromHex(hex, out var color));
        Assert.Equal(ArgbColor.FromRgb(r, g, b), color);
    }

    [Fact]
    public void ArgbColor_round_trips_hex()
    {
        Assert.Equal("#1A2B3C", ArgbColor.FromRgb(0x1A, 0x2B, 0x3C).ToHex());
    }

    [Theory]
    [InlineData("General", NumberFormatKind.General)]
    [InlineData("0.00", NumberFormatKind.Number)]
    [InlineData("0%", NumberFormatKind.Percent)]
    [InlineData("$#,##0.00", NumberFormatKind.Currency)]
    [InlineData("yyyy-mm-dd", NumberFormatKind.Date)]
    [InlineData("hh:mm:ss", NumberFormatKind.Time)]
    [InlineData("@", NumberFormatKind.Text)]
    public void NumberFormat_classifies(string code, NumberFormatKind expected)
    {
        Assert.Equal(expected, new NumberFormat(code).Kind);
    }

    [Theory]
    [InlineData("0.00", 3.14159, "3.14")]
    [InlineData("0%", 0.25, "25%")]
    [InlineData("#,##0", 1234567, "1,234,567")]
    [InlineData("General", 42, "42")]
    [InlineData("General", 1.5, "1.5")]
    public void NumberFormatter_formats_numbers(string code, double value, string expected)
    {
        Assert.Equal(expected, NumberFormatter.Format(CellValue.Number(value), code, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NumberFormatter_formats_dates()
    {
        // Serial for 2026-06-07 via DATE-style construction.
        double serial = Cellestra.Core.Formulas.Evaluation.DateConversion.DateToSerial(2026, 6, 7);
        Assert.Equal("2026-06-07", NumberFormatter.Format(CellValue.Number(serial), "yyyy-mm-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NumberFormatter_passes_errors_and_text_through()
    {
        Assert.Equal("#DIV/0!", NumberFormatter.Format(CellValue.FromError(CellError.DivideByZero()), "0.00", CultureInfo.InvariantCulture));
        Assert.Equal("hello", NumberFormatter.Format(CellValue.Text("hello"), "0.00", CultureInfo.InvariantCulture));
    }
}
