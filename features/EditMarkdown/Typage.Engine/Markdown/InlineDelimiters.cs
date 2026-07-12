namespace Typage.Engine.Markdown;

/// <summary>
/// One paired-delimiter construct found inside a stretch of inline content: the opening run starts at
/// <see cref="OpenStart"/>, the closing run at <see cref="InnerEnd"/>, each <see cref="MarkerLen"/> chars
/// of <see cref="Delim"/> long.
/// </summary>
public readonly record struct InlineDelimiterSpan(int OpenStart, int InnerEnd, int MarkerLen, char Delim)
{
    /// <summary>First column of the construct's content (just past the opening run).</summary>
    public int InnerStart => OpenStart + MarkerLen;

    /// <summary>One past the construct's closing run — the end of its whole source span.</summary>
    public int End => InnerEnd + MarkerLen;
}

/// <summary>
/// Finds the paired inline delimiters — <c>~~</c>, <c>==</c>, and <c>*</c>/<c>_</c> emphasis (a run of 1–3) —
/// inside a stretch of content, with the same tolerance the tokenizer applies (no whitespace just inside the
/// delimiters, a closing run at least as long as the opening one).
///
/// <para>It exists because the tokenizer emits emphasis / strike / highlight <b>content as one flat token</b>
/// (PRD §8.4: no inline recursion inside emphasis) — so in <c>***~~bold~~***</c> the <c>~~</c> is just part of
/// the StrongEmphasis content's text, with no token of its own. Anything that has to see one level further in
/// scans for it here: the surface splits such content to <i>draw</i> the nested strike, and the formatting
/// toggles look here to find the run a second ⌘⇧X should remove. One scanner, so what is drawn as struck and
/// what un-strikes are the same span.</para>
/// </summary>
public static class InlineDelimiters
{
    /// <summary>The top-level paired constructs inside <paramref name="content"/>, in source order. One level
    /// deep: the content of a construct found here is not rescanned.</summary>
    public static List<InlineDelimiterSpan> FindNested(string content)
    {
        var found = new List<InlineDelimiterSpan>();
        var i = 0;
        while (i < content.Length)
        {
            var c = content[i];
            if (c is '~' or '=' or '*' or '_')
            {
                var run = DelimRun(content, i, c);
                var count = c is '~' or '=' ? 2 : Math.Min(run, 3);
                var innerStart = i + count;
                if (run >= count && innerStart < content.Length
                    && content[innerStart] != c && !char.IsWhiteSpace(content[innerStart]))
                {
                    var close = FindClose(content, innerStart, c, count);
                    if (close > innerStart)
                    {
                        found.Add(new InlineDelimiterSpan(i, close, count, c));
                        i = close + count;
                        continue;
                    }
                }
            }

            i++;
        }

        return found;
    }

    /// <summary>Index of the closing delimiter run (≥ <paramref name="count"/> of <paramref name="c"/>, not
    /// preceded by whitespace) at or after <paramref name="from"/>; -1 if none.</summary>
    private static int FindClose(string content, int from, char c, int count)
    {
        var j = from;
        while (j < content.Length)
        {
            if (content[j] == c && !char.IsWhiteSpace(content[j - 1]))
            {
                if (DelimRun(content, j, c) >= count)
                {
                    return j;
                }

                j += DelimRun(content, j, c);
            }
            else
            {
                j++;
            }
        }

        return -1;
    }

    private static int DelimRun(string s, int i, char c)
    {
        var n = 0;
        while (i + n < s.Length && s[i + n] == c)
        {
            n++;
        }

        return n;
    }
}
