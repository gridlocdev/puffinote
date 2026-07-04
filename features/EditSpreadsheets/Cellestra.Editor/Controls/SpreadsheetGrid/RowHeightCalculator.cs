namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Pure geometry for how tall a row must be so its text isn't clipped. The grid grows a row to fit
/// the tallest font any of its cells uses (task: "the height of a row should change when the font
/// size is increased in a cell to avoid overlapping"), never shrinking below the default row height.
/// Kept free of Avalonia so it can be unit-tested headlessly.
/// </summary>
internal static class RowHeightCalculator
{
    // A point is 96/72 device-independent pixels.
    private const double PointToPixel = 96.0 / 72.0;

    // Glyphs sit in a box a bit taller than the nominal font size (ascender + descender + leading).
    // ~1.2 approximates how FormattedText lays a single line out, tuned so the 10pt default lands
    // exactly on the 20px default row: 10 * (96/72) * 1.2 + 2*2 = 20, while larger fonts grow it.
    private const double LineFactor = 1.2;

    /// <summary>
    /// The natural pixel height for a single cell at <paramref name="fontSizePoints"/>, including the
    /// top and bottom vertical padding (<paramref name="verticalPadding"/> per side).
    /// </summary>
    internal static double ForFontSize(double fontSizePoints, double verticalPadding)
    {
        double glyphBox = fontSizePoints * PointToPixel * LineFactor;
        return Math.Ceiling(glyphBox + 2 * verticalPadding);
    }

    /// <summary>
    /// The natural pixel height for a wrapped cell with <paramref name="lineCount"/> text lines at
    /// <paramref name="fontSizePoints"/>, including the top and bottom vertical padding. A single line
    /// matches <see cref="ForFontSize"/>; each extra line adds one more glyph box (task 6: a wrapped
    /// cell grows its row to fit every line).
    /// </summary>
    internal static double ForWrappedLines(int lineCount, double fontSizePoints, double verticalPadding)
    {
        int lines = Math.Max(1, lineCount);
        double glyphBox = fontSizePoints * PointToPixel * LineFactor;
        return Math.Ceiling(glyphBox * lines + 2 * verticalPadding);
    }

    /// <summary>
    /// The effective height for a row: the larger of the row's explicit/default height, the height the
    /// tallest font in the row needs, and the height any wrapped cell in the row needs. So increasing a
    /// cell's font size (or wrapping its text onto more lines) grows the row, while a hand-resized
    /// (taller) row keeps its size.
    /// </summary>
    internal static double Effective(double rowHeight, double tallestFontPoints, double verticalPadding, double wrappedPixels = 0)
    {
        double effective = rowHeight;
        if (tallestFontPoints > 0)
        {
            effective = Math.Max(effective, ForFontSize(tallestFontPoints, verticalPadding));
        }

        if (wrappedPixels > 0)
        {
            effective = Math.Max(effective, wrappedPixels);
        }

        return effective;
    }
}
