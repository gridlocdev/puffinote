using System.Globalization;

namespace Typage.Engine.Document;

/// <summary>
/// Grapheme-cluster boundary math (PRD §7.2 / §9.7). Caret movement, selection, and
/// deletion operate on <i>grapheme clusters</i> — what a reader perceives as one
/// character — so emoji, combining marks, surrogate pairs, and even <c>\r\n</c> move and
/// delete as single units. Built on <see cref="StringInfo"/> (System.Globalization), so the
/// engine takes no UI or third-party dependency for Unicode segmentation.
/// </summary>
public static class Grapheme
{
    /// <summary>The next grapheme boundary strictly greater than <paramref name="index"/> (clamped to the end).</summary>
    public static int NextBoundary(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (index < 0)
        {
            return 0;
        }

        if (index >= text.Length)
        {
            return text.Length;
        }

        var step = StringInfo.GetNextTextElement(text, index).Length;
        return Math.Min(text.Length, index + Math.Max(1, step));
    }

    /// <summary>The previous grapheme boundary strictly less than <paramref name="index"/> (clamped to 0).</summary>
    public static int PreviousBoundary(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (index <= 0)
        {
            return 0;
        }

        if (index > text.Length)
        {
            index = text.Length;
        }

        // Walk grapheme boundaries from the start, remembering the last one before index.
        var prev = 0;
        var pos = 0;
        while (pos < index)
        {
            var step = Math.Max(1, StringInfo.GetNextTextElement(text, pos).Length);
            if (pos + step >= index)
            {
                return pos;
            }

            prev = pos;
            pos += step;
        }

        return prev;
    }

    /// <summary>Count grapheme clusters in <paramref name="text"/> (used for character counts).</summary>
    public static int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            count++;
        }

        return count;
    }
}
