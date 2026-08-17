namespace Cellestra.Core.Styling;

/// <summary>
/// Measures how wide a run of text renders in a given font, in device-independent pixels.
/// Auto-fitting a column is the one piece of layout that needs a font stack, and Core has none
/// (Golden Rule #1) — so the caller supplies one: the app passes an Avalonia-backed measurer that
/// asks the real typeface, and headless callers (the <c>cellestra</c> CLI, Core tests) fall back to
/// <see cref="ApproximateTextMeasurer"/>.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>The width of <paramref name="text"/> drawn in <paramref name="font"/>, unzoomed.</summary>
    double Measure(string text, Font font);
}

/// <summary>
/// A font-stack-free estimate of text width: every character contributes a fraction of the em,
/// bucketed by how wide that character is in a humanist sans like Helvetica Neue (the default of
/// PRD §8.8). It is an estimate and says so — a column fitted by the CLI can sit a few pixels off
/// what the app would measure — but it needs no display, never varies by machine, and is what keeps
/// a headless <c>cellestra clean-up</c> from leaving every column at the default width.
/// </summary>
public sealed class ApproximateTextMeasurer : ITextMeasurer
{
    public static ApproximateTextMeasurer Instance { get; } = new();

    public double Measure(string text, Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        double em = font.SizePoints * 96d / 72d;
        double units = 0;
        foreach (char c in text)
        {
            units += Advance(c);
        }

        // Bold sets slightly wider than regular in most families; italic does not, materially.
        return units * em * (font.Bold ? 1.04d : 1d);
    }

    /// <summary>The character's advance as a fraction of the em, bucketed by shape.</summary>
    private static double Advance(char c) => c switch
    {
        'i' or 'j' or 'l' or 'I' or '.' or ',' or ':' or ';' or '\'' or '`' or '|' or '!' or '(' or ')' or '[' or ']' or '/' or '\\' => 0.28,
        'f' or 't' or 'r' or ' ' or '-' => 0.34,
        'm' or 'w' or 'M' or 'W' or '@' or '%' => 0.88,
        >= 'A' and <= 'Z' => 0.68,
        >= '0' and <= '9' => 0.556,
        _ => 0.53,
    };
}
