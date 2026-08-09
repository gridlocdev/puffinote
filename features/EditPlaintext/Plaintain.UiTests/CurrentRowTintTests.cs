using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Plaintain.Control.Controls;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// The caret's row is filled edge-to-edge across the viewport, so its tint is doing a different job from
/// Typage's — there the fill stops at the content column, here it is a bar across the whole window on
/// every row the caret visits. It has to be findable at a glance and invisible the rest of the time.
/// </summary>
public class CurrentRowTintTests
{
    /// <summary>
    /// Faint enough not to compete with the text. The numbers are the point: anything much above these
    /// is the bar people asked to have turned down, and zero is not a highlight at all.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, 0x0A)]
    [InlineData(true, 0x14)]
    public void The_caret_row_is_tinted_faintly_in_both_variants(bool dark, byte alpha)
    {
        var tint = PlainTextEditor.CurrentRowTintForTest(dark ? ThemeVariant.Dark : ThemeVariant.Light);

        Assert.Equal(alpha, tint.A);
        Assert.InRange(tint.A, 1, 0x18);
    }

    /// <summary>Still a tint of the canvas, not a colour of its own — white on dark, black on light.</summary>
    [AvaloniaFact]
    public void The_tint_is_the_canvas_lightened_or_darkened_rather_than_a_hue()
    {
        var dark = PlainTextEditor.CurrentRowTintForTest(ThemeVariant.Dark);
        var light = PlainTextEditor.CurrentRowTintForTest(ThemeVariant.Light);

        Assert.Equal((0xFF, 0xFF, 0xFF), (dark.R, dark.G, dark.B));
        Assert.Equal((0x00, 0x00, 0x00), (light.R, light.G, light.B));
    }
}
