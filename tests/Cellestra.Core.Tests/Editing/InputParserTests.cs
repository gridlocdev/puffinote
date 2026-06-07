using System.Globalization;
using Cellestra.Core.Editing;
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
}
