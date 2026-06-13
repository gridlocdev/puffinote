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

    [Theory]
    // 0.520834 of a day ≈ 12:30:00. AM/PM must render as the literal "PM" (12-hour clock), not the
    // month-number "A1/P1"; "mm" between hours and seconds is minutes, not the month.
    [InlineData("h:mm:ss AM/PM", 0.520834, "12:30:00 PM")]
    [InlineData("h:mm AM/PM", 0.1, "2:24 AM")]
    [InlineData("hh:mm:ss", 0.520834, "12:30:00")]
    public void NumberFormatter_formats_times(string code, double value, string expected)
    {
        Assert.Equal(expected, NumberFormatter.Format(CellValue.Number(value), code, CultureInfo.InvariantCulture));
    }

    [Theory]
    // 0.520834 of a day ≈ 12:30:00. A single-character date/time code must not be read as a .NET
    // *standard* specifier ("H" throws; "d"/"m"/"s" render the wrong field) — it crashed the grid
    // paint / TEXT() path. The bracketed elapsed-hours form keeps "mm" as minutes, not the month.
    [InlineData("h", 0.520834, "12")]
    [InlineData("s", 0.520834, "0")]
    [InlineData("[h]:mm", 0.520834, "[12]:30")]
    public void NumberFormatter_handles_single_char_and_bracketed_time_codes(string code, double value, string expected)
    {
        Assert.Equal(expected, NumberFormatter.Format(CellValue.Number(value), code, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NumberFormatter_renders_date_time_components_together()
    {
        double serial = Cellestra.Core.Formulas.Evaluation.DateConversion.DateToSerial(2026, 6, 7) + 0.520834;
        Assert.Equal("2026-06-07 12:30:00", NumberFormatter.Format(CellValue.Number(serial), "yyyy-mm-dd hh:mm:ss", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NumberFormatter_passes_errors_and_text_through()
    {
        Assert.Equal("#DIV/0!", NumberFormatter.Format(CellValue.FromError(CellError.DivideByZero()), "0.00", CultureInfo.InvariantCulture));
        Assert.Equal("hello", NumberFormatter.Format(CellValue.Text("hello"), "0.00", CultureInfo.InvariantCulture));
    }

    [Theory]
    // A serial outside the representable date range must not throw — it falls back to a plain number
    // (LibreOffice shows such out-of-range serials numerically, not as #### or an exception).
    [InlineData(99999999.0)]
    [InlineData(-800000.0)]
    public void NumberFormatter_falls_back_on_out_of_range_date_serial(double serial)
    {
        // No exception, and the result is the general-number rendering rather than a date string.
        string expected = NumberFormatter.FormatGeneral(serial, CultureInfo.InvariantCulture);
        Assert.Equal(expected, NumberFormatter.Format(CellValue.Number(serial), "yyyy-mm-dd", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0", "0.0")]
    [InlineData("0.00", "0.000")]
    [InlineData("#,##0.00", "#,##0.000")]
    [InlineData("$#,##0.00", "$#,##0.000")]
    [InlineData("0.00%", "0.000%")]
    [InlineData("0.00E+00", "0.000E+00")] // only the mantissa grows; the exponent's "00" is left alone
    [InlineData("General", "#,##0.0")]    // General seeds from the grouped number, then steps up
    public void NumberFormatCode_increase_adds_a_decimal(string code, string expected)
    {
        Assert.Equal(expected, NumberFormatCode.IncreaseDecimals(code));
    }

    [Theory]
    [InlineData("0.000", "0.00")]
    [InlineData("0.0", "0")]
    [InlineData("0", "0")]            // already none — clamps, no negative
    [InlineData("#,##0.00", "#,##0.0")]
    [InlineData("$#,##0.0", "$#,##0")]
    [InlineData("0.00E+00", "0.0E+00")]
    [InlineData("General", "#,##0")]  // General has no decimals; stays at zero
    public void NumberFormatCode_decrease_removes_a_decimal(string code, string expected)
    {
        Assert.Equal(expected, NumberFormatCode.DecreaseDecimals(code));
    }

    [Theory]
    [InlineData("yyyy-mm-dd")]
    [InlineData("hh:mm:ss")]
    [InlineData("@")]
    public void NumberFormatCode_leaves_non_numeric_codes_untouched(string code)
    {
        Assert.Equal(code, NumberFormatCode.IncreaseDecimals(code));
        Assert.Equal(code, NumberFormatCode.DecreaseDecimals(code));
    }
}
