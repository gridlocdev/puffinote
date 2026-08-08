using System.Globalization;

namespace Puffinote.Components.Theming;

/// <summary>
/// The one zoom ladder every app in the family steps along, so ⌘= / ⌘− mean the same amount of "bigger"
/// in a note, a sheet, a log, and a board (Cellestra PRD §4 fixes the endpoints at 50% and 400%).
///
/// <para>The stops are a ratio ladder rather than a fixed ±10%: a linear step is coarse where it matters
/// (50% → 60% is a fifth of the page) and imperceptible where it doesn't (390% → 400%). Browsers settled
/// on this shape for the same reason, and a reader who knows one app's zoom then knows all five.</para>
///
/// <para>What zoom scales is the <b>document</b> — the text, the cells, the cards — never the chrome around
/// it. A menu bar or a filter strip that grew with the content would take back the room the reader zoomed
/// out to gain.</para>
/// </summary>
public static class ZoomScale
{
    /// <summary>The unzoomed size: 100%.</summary>
    public const double Default = 1.0d;

    /// <summary>Smallest zoom factor — 50%.</summary>
    public const double Min = 0.5d;

    /// <summary>Largest zoom factor — 400%.</summary>
    public const double Max = 4.0d;

    /// <summary>
    /// The stops <see cref="In"/> and <see cref="Out"/> step between, ascending. Tighter around 100%, where
    /// a reader is nudging for comfort, and wider at the ends, where they are reframing the whole document.
    /// </summary>
    public static readonly IReadOnlyList<double> Stops =
        [0.5d, 0.67d, 0.75d, 0.8d, 0.9d, 1.0d, 1.1d, 1.25d, 1.5d, 1.75d, 2.0d, 2.5d, 3.0d, 4.0d];

    // Zoom factors arrive from JSON settings and from arithmetic, so "is this already the 1.25 stop?" has to
    // tolerate the last bit or two of a double. Half a percent is far below anything anyone can see, and far
    // above any rounding error.
    private const double Epsilon = 0.005d;

    /// <summary>Constrain <paramref name="zoom"/> to [<see cref="Min"/>, <see cref="Max"/>]; a
    /// non-finite value (a corrupt settings file) falls back to <see cref="Default"/>.</summary>
    public static double Clamp(double zoom) =>
        double.IsFinite(zoom) ? Math.Clamp(zoom, Min, Max) : Default;

    /// <summary>
    /// The next stop above <paramref name="zoom"/> — or <see cref="Max"/> when there is none. An off-ladder
    /// value (say a 1.3 restored from an older build) steps to the first stop genuinely above it, so zooming
    /// in never appears to zoom out.
    /// </summary>
    public static double In(double zoom)
    {
        double current = Clamp(zoom);
        foreach (double stop in Stops)
        {
            if (stop > current + Epsilon)
            {
                return stop;
            }
        }

        return Max;
    }

    /// <summary>The next stop below <paramref name="zoom"/> — or <see cref="Min"/> when there is none.</summary>
    public static double Out(double zoom)
    {
        double current = Clamp(zoom);
        for (int i = Stops.Count - 1; i >= 0; i--)
        {
            if (Stops[i] < current - Epsilon)
            {
                return Stops[i];
            }
        }

        return Min;
    }

    /// <summary>The zoom as a percentage for a status bar or menu — "100%", "125%".</summary>
    public static string Format(double zoom) =>
        string.Create(CultureInfo.InvariantCulture, $"{Clamp(zoom) * 100:0}%");
}
