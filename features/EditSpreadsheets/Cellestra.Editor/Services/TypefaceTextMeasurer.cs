using System.Globalization;
using Avalonia.Media;
using Cellestra.Core.Styling;

namespace Cellestra.Desktop.Services;

/// <summary>
/// The real-typeface <see cref="ITextMeasurer"/>: it asks the same <see cref="FormattedText"/> the
/// grid draws with, so a column Clean Up fits and a column fitted by double-clicking its header
/// border land on the same width. Core carries the font-free estimate
/// (<see cref="ApproximateTextMeasurer"/>) for callers with no display; this is the one the app uses.
/// </summary>
public sealed class TypefaceTextMeasurer : ITextMeasurer
{
    public static TypefaceTextMeasurer Instance { get; } = new();

    public double Measure(string text, Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var typeface = new Typeface(
            font.Family,
            font.Italic ? FontStyle.Italic : FontStyle.Normal,
            font.Bold ? FontWeight.Bold : FontWeight.Normal);

        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, font.SizePoints * 96.0 / 72.0, Brushes.Black).Width;
    }
}
