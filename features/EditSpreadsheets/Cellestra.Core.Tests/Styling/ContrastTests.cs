using Cellestra.Core.Styling;
using Xunit;

namespace Cellestra.Core.Tests.Styling;

public class ContrastTests
{
    [Fact]
    public void Black_on_white_is_the_maximum_and_a_colour_on_itself_is_the_minimum()
    {
        Assert.Equal(21.0, Contrast.Ratio(ArgbColor.Black, ArgbColor.White), 2);
        Assert.Equal(1.0, Contrast.Ratio(ArgbColor.White, ArgbColor.White), 6);
    }

    [Fact]
    public void The_ratio_does_not_depend_on_which_colour_is_the_background()
    {
        var ink = ArgbColor.FromRgb(0x15, 0x73, 0x4F);
        var paper = ArgbColor.FromRgb(0xD9, 0xE5, 0xF5);
        Assert.Equal(Contrast.Ratio(ink, paper), Contrast.Ratio(paper, ink), 9);
    }

    [Fact]
    public void White_on_the_table_header_band_is_below_the_readable_floor()
    {
        // The case the inline editor's legibility guard exists for: a header styled white, over the
        // light-theme header band, is not a low-contrast choice — it is an invisible one.
        Assert.True(Contrast.Ratio(ArgbColor.White, ArgbColor.FromRgb(0xD9, 0xE5, 0xF5)) < 3.0);
        Assert.True(Contrast.Ratio(ArgbColor.Black, ArgbColor.FromRgb(0xD9, 0xE5, 0xF5)) > 3.0);
    }

    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0xFF, 1.0)]
    public void Luminance_spans_black_to_white(byte channel, double expected) =>
        Assert.Equal(expected, Contrast.RelativeLuminance(ArgbColor.FromRgb(channel, channel, channel)), 6);
}
