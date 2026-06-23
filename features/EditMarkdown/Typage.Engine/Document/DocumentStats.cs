namespace Typage.Engine.Document;

/// <summary>The line-ending convention a document uses (PRD §7.1, §7.5 — surfaced in the status bar).</summary>
public enum NewlineStyle
{
    Lf,
    CrLf,
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

    /// <summary>Short label for the line-ending style.</summary>
    public string NewlineLabel => Newline == NewlineStyle.CrLf ? "CRLF" : "LF";

    public static DocumentStats Compute(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = document.GetText();
        var words = CountWords(text);
        var characters = Grapheme.Count(text);
        var lines = document.LineCount;
        var minutes = words == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerMinute));
        return new DocumentStats(words, characters, lines, minutes, DetectNewline(text));
    }

    private static NewlineStyle DetectNewline(string text)
    {
        var i = text.IndexOf('\n');
        return i > 0 && text[i - 1] == '\r' ? NewlineStyle.CrLf : NewlineStyle.Lf;
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
