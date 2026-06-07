using System.Globalization;
using Cellestra.Core.Editing;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class InputParserTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("'007", "007")]
    [InlineData("'=notaformula", "=notaformula")]
    [InlineData("'5", "5")]
    [InlineData("'TRUE", "TRUE")]
    [InlineData("''double", "'double")] // only the first apostrophe is the flag
    public void Leading_apostrophe_forces_text(string typed, string stored)
    {
        var parsed = InputParser.Parse(typed, EnUs);

        Assert.False(parsed.IsFormula);
        Assert.True(parsed.Value.IsText);
        Assert.Equal(stored, parsed.Value.AsText); // apostrophe stripped, not displayed
    }

    [Fact]
    public void Bare_number_still_parses_as_number()
    {
        Assert.True(InputParser.Parse("007", EnUs).Value.IsNumber);
        Assert.Equal(7, InputParser.Parse("007", EnUs).Value.AsNumber);
    }

    [Fact]
    public void Slash_date_coerces_to_serial_with_date_format()
    {
        var parsed = InputParser.Parse("6/7/2026", EnUs);

        Assert.True(parsed.Value.IsNumber);
        Assert.Equal(DateConversion.ToSerial(new System.DateTime(2026, 6, 7)), parsed.Value.AsNumber, 9);
        Assert.NotNull(parsed.SuggestedFormat);
        Assert.Equal(NumberFormatKind.Date, parsed.SuggestedFormat!.Kind);
    }

    [Fact]
    public void Iso_date_coerces_to_serial()
    {
        var parsed = InputParser.Parse("2026-06-07", EnUs);

        Assert.True(parsed.Value.IsNumber);
        Assert.Equal(DateConversion.ToSerial(new System.DateTime(2026, 6, 7)), parsed.Value.AsNumber, 9);
        Assert.Equal(NumberFormatKind.Date, parsed.SuggestedFormat!.Kind);
    }

    [Fact]
    public void Date_parsing_follows_locale_day_month_order()
    {
        var deDe = CultureInfo.GetCultureInfo("de-DE");
        // 03/04/2026 is 3 April in de-DE, 4 March in en-US (#38).
        Assert.Equal(
            DateConversion.ToSerial(new System.DateTime(2026, 4, 3)),
            InputParser.Parse("03/04/2026", deDe).Value.AsNumber, 9);
        Assert.Equal(
            DateConversion.ToSerial(new System.DateTime(2026, 3, 4)),
            InputParser.Parse("03/04/2026", EnUs).Value.AsNumber, 9);
    }

    [Fact]
    public void Plain_text_and_codes_are_not_coerced_to_dates()
    {
        Assert.True(InputParser.Parse("May", EnUs).Value.IsText);     // month name alone stays text
        Assert.True(InputParser.Parse("part-2", EnUs).Value.IsText);  // hyphenated token stays text
        Assert.Null(InputParser.Parse("hello", EnUs).SuggestedFormat);
    }
}
