namespace Puffinote.Text.Document;

/// <summary>The line-ending convention a document uses (PRD §7.1, §7.5 — surfaced in the status bar).</summary>
public enum NewlineStyle
{
    Lf,
    CrLf,
}

/// <summary>
/// How many of each kind of line ending a document has. A file with both is usually a merge artefact or
/// a tool that appended in the wrong convention — and sometimes it is deliberate, which is why this
/// reports rather than decides.
/// </summary>
/// <param name="Lf">Bare <c>\n</c> endings.</param>
/// <param name="CrLf">Windows <c>\r\n</c> endings.</param>
public readonly record struct LineEndingCounts(int Lf, int CrLf)
{
    /// <summary>True when the document uses both conventions.</summary>
    public bool IsMixed => Lf > 0 && CrLf > 0;

    /// <summary>The convention most of the file uses — the one normalising should default to. Ties go to
    /// LF, which is what a file with no line endings at all is treated as everywhere else here.</summary>
    public NewlineStyle Dominant => CrLf > Lf ? NewlineStyle.CrLf : NewlineStyle.Lf;

    /// <summary>Count the endings in <paramref name="text"/> in one pass.</summary>
    public static LineEndingCounts Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int lf = 0, crlf = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            // A lone \r (classic Mac) is not counted as an ending at all: nothing in this family produces
            // one, and treating a stray carriage return inside a line as a line break would be worse than
            // ignoring it.
            if (i > 0 && text[i - 1] == '\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        return new LineEndingCounts(lf, crlf);
    }
}

/// <summary>Rewriting a document's line endings — the operation behind "Normalise Line Endings".</summary>
public static class LineEndings
{
    /// <summary>The literal characters <paramref name="style"/> is written with.</summary>
    public static string TextFor(NewlineStyle style) => style == NewlineStyle.CrLf ? "\r\n" : "\n";

    /// <summary>
    /// <paramref name="text"/> with every line ending written as <paramref name="style"/>. Returns the
    /// same string when nothing needs to change, so a caller can skip an edit that would only clear the
    /// document's undo position and mark it dirty for no reason.
    /// </summary>
    public static string Normalize(string text, NewlineStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        var counts = LineEndingCounts.Of(text);
        if (style == NewlineStyle.CrLf ? counts.Lf == 0 : counts.CrLf == 0)
        {
            return text;
        }

        // Via LF so a \r\n is never doubled into \r\r\n on the way through.
        string lf = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return style == NewlineStyle.CrLf ? lf.Replace("\n", "\r\n", StringComparison.Ordinal) : lf;
    }
}

/// <summary>
/// Live document statistics for the status bar (PRD §7.5, heuristic 1) and the CLI's
/// <c>stats</c> command — computed once here so the two never disagree. Character count is in
/// grapheme clusters (what a reader perceives as characters), not UTF-16 code units.
/// </summary>
public readonly record struct DocumentStats(int Words, int Characters, int Lines, int ReadingMinutes, NewlineStyle Newline)
{
    /// <summary>Average adult silent reading speed, words per minute (used for the reading-time estimate).</summary>
    public const int WordsPerMinute = 200;

    /// <summary>How many endings of each kind the document has — the basis of <see cref="Newline"/>.</summary>
    public LineEndingCounts NewlineCounts { get; init; }

    /// <summary>True when the document uses both conventions at once.</summary>
    public bool HasMixedNewlines => NewlineCounts.IsMixed;

    /// <summary>
    /// Short label for the line-ending style — with "mixed" appended when the document has both, because
    /// "CRLF" alone is a claim about the file that would not be true.
    /// </summary>
    public string NewlineLabel => (Newline == NewlineStyle.CrLf ? "CRLF" : "LF") + (HasMixedNewlines ? " (mixed)" : string.Empty);

    public static DocumentStats Compute(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = document.GetText();
        var words = CountWords(text);
        var characters = Grapheme.Count(text);
        var lines = document.LineCount;
        var minutes = words == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerMinute));
        var counts = LineEndingCounts.Of(text);
        return new DocumentStats(words, characters, lines, minutes, counts.Dominant) { NewlineCounts = counts };
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }
}
