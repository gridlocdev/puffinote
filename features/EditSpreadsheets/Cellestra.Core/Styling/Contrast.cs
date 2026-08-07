namespace Cellestra.Core.Styling;

/// <summary>
/// Relative luminance and contrast ratio, per WCAG 2.2 §1.4.3. Used to answer one question the grid
/// cannot answer from the style alone: <i>would this text actually be readable on the surface it is
/// about to be drawn on?</i> A cell's font colour is document data and is honoured as-is when it is
/// drawn over the fill it was chosen against — but the inline editor paints its own surface, and a
/// white font over a white editor is not a colour choice, it is an invisible cell.
/// </summary>
public static class Contrast
{
    /// <summary>
    /// The ratio between two colours, from 1 (identical) to 21 (black on white). Symmetric, so the
    /// order of the arguments doesn't matter. Alpha is ignored — these are opaque paint colours.
    /// </summary>
    public static double Ratio(ArgbColor a, ArgbColor b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>The WCAG relative luminance of <paramref name="color"/>, from 0 (black) to 1 (white).</summary>
    public static double RelativeLuminance(ArgbColor color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    /// <summary>One 8-bit sRGB channel, gamma-expanded to the linear value luminance is summed over.</summary>
    private static double Linear(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
