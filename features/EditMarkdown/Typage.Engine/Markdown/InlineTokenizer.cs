namespace Typage.Engine.Markdown;

/// <summary>
/// Tokenizes the inline constructs inside a single line's content region (emphasis, code,
/// links, images, autolinks, wiki-links, embeds, escapes, strikethrough, highlight). The
/// scanner is <b>tolerant</b> (PRD §7.4): an unmatched opening marker renders as literal text
/// until validly closed, so styling only applies on a complete construct and typing never
/// errors or flickers.
/// </summary>
/// <remarks>
/// To keep the dialect a documented, predictable subset (PRD §8.4–§8.5), emphasis/strong/
/// strike/highlight content is a single typed token (no recursion); link <i>text</i> recurses
/// so emphasis inside a link still styles. Full CommonMark emphasis precedence and nested
/// inline-in-emphasis are explicit non-goals.
/// </remarks>
internal sealed class InlineTokenizer
{
    private readonly string _text;
    private readonly int _absStart;
    private readonly List<Token> _out;
    private int _pendingTextStart = -1;
    private int _nextNode = 1; // per-line node id; each construct claims one (shared across link-text recursion)

    private InlineTokenizer(string text, int absStart, List<Token> output)
    {
        _text = text;
        _absStart = absStart;
        _out = output;
    }

    /// <summary>
    /// Tokenize <c>text[from, to)</c>, appending tokens (absolute offsets) to <paramref name="output"/>.
    /// When <paramref name="detectHardBreak"/> is set (line-level content, not nested link text), a
    /// trailing run of two-or-more spaces or a trailing backslash is emitted as a <see cref="TokenType.HardBreak"/>.
    /// </summary>
    public static void Tokenize(string text, int absStart, int from, int to, List<Token> output, bool detectHardBreak = false)
    {
        var contentTo = to;
        var breakStart = -1;
        if (detectHardBreak && to > from)
        {
            if (text[to - 1] == '\\')
            {
                breakStart = to - 1; // trailing backslash hard break
            }
            else
            {
                var s = to;
                while (s > from && text[s - 1] == ' ')
                {
                    s--;
                }

                if (to - s >= 2 && s > from)
                {
                    breakStart = s; // two-or-more trailing spaces, with content before them
                }
            }
        }

        if (breakStart >= 0)
        {
            contentTo = breakStart;
        }

        var tokenizer = new InlineTokenizer(text, absStart, output);
        tokenizer.Run(from, contentTo);
        if (breakStart >= 0)
        {
            output.Add(new Token(absStart + breakStart, to - breakStart, TokenType.HardBreak, TokenRole.Marker));
        }
    }

    private void Run(int from, int to)
    {
        var i = from;
        while (i < to)
        {
            var consumed = TryConstruct(i, to);
            if (consumed > 0)
            {
                i += consumed;
            }
            else
            {
                AppendLiteral(i);
                i++;
            }
        }

        FlushText(to);
    }

    /// <summary>Try to recognize a construct starting at <paramref name="i"/>; return its length, or 0.</summary>
    private int TryConstruct(int i, int to)
    {
        var c = _text[i];
        return c switch
        {
            '\\' => TryEscape(i, to),
            '`' => TryInlineCode(i, to),
            '!' => TryImageOrEmbed(i, to),
            '[' => TryLinkOrWiki(i, to),
            '<' => TryAutolink(i, to),
            '~' => TryDelimited(i, to, '~', 2, 2, TokenType.Strikethrough),
            '=' => TryDelimited(i, to, '=', 2, 2, TokenType.Highlight),
            '*' or '_' => TryEmphasis(i, to, c),
            'h' or 'H' => TryBareUrl(i, to),
            _ => 0,
        };
    }

    private int TryEscape(int i, int to)
    {
        if (i + 1 >= to)
        {
            return 0;
        }

        var n = _text[i + 1];
        if (!IsAsciiPunctuation(n))
        {
            return 0;
        }

        var node = _nextNode++;
        FlushText(i);
        Emit(i, 1, TokenType.Escape, TokenRole.Marker, node: node);     // the backslash
        Emit(i + 1, 1, TokenType.Escape, TokenRole.Content, node: node); // the literal char
        return 2;
    }

    private int TryInlineCode(int i, int to)
    {
        var run = RunLength(i, to, '`');
        var contentStart = i + run;
        // Find a closing run of exactly `run` backticks.
        var j = contentStart;
        while (j < to)
        {
            if (_text[j] == '`')
            {
                var closeRun = RunLength(j, to, '`');
                if (closeRun == run)
                {
                    if (j == contentStart)
                    {
                        return 0; // empty `` `` — treat as literal
                    }

                    var node = _nextNode++;
                    FlushText(i);
                    Emit(i, run, TokenType.Marker, TokenRole.Marker, node: node);
                    Emit(contentStart, j - contentStart, TokenType.InlineCode, TokenRole.Content, node: node);
                    Emit(j, run, TokenType.Marker, TokenRole.Marker, node: node);
                    return j + run - i;
                }

                j += closeRun;
            }
            else
            {
                j++;
            }
        }

        return 0; // unterminated -> literal
    }

    private int TryImageOrEmbed(int i, int to)
    {
        // ![[target]] embed
        if (i + 2 < to && _text[i + 1] == '[' && _text[i + 2] == '[')
        {
            var close = IndexOf("]]", i + 3, to);
            if (close > i + 3)
            {
                var node = _nextNode++;
                FlushText(i);
                Emit(i, 3, TokenType.Marker, TokenRole.Marker, node: node);
                var target = _text.Substring(i + 3, close - (i + 3));
                Emit(i + 3, close - (i + 3), TokenType.Embed, TokenRole.Content, target, node);
                Emit(close, 2, TokenType.Marker, TokenRole.Marker, node: node);
                return close + 2 - i;
            }

            return 0;
        }

        // ![alt](url) image
        if (i + 1 < to && _text[i + 1] == '[')
        {
            var altClose = MatchBracket(i + 1, to);
            if (altClose > 0 && altClose + 1 < to && _text[altClose + 1] == '(')
            {
                var urlClose = MatchParen(altClose + 1, to);
                if (urlClose > 0)
                {
                    var node = _nextNode++;
                    var altStart = i + 2;
                    var (urlStart, urlLen) = Destination(altClose + 2, urlClose);
                    FlushText(i);
                    Emit(i, 2, TokenType.Marker, TokenRole.Marker, node: node); // ![
                    var alt = _text.Substring(altStart, altClose - altStart);
                    Emit(altStart, altClose - altStart, TokenType.ImageAlt, TokenRole.Content, alt, node);
                    Emit(altClose, 2, TokenType.Marker, TokenRole.Marker, node: node); // ](
                    var url = _text.Substring(urlStart, urlLen);
                    Emit(urlStart, urlLen, TokenType.ImageUrl, TokenRole.Marker, url, node);
                    // Closing marker covers any title (e.g. "alt") plus the ')'.
                    Emit(urlStart + urlLen, urlClose + 1 - (urlStart + urlLen), TokenType.Marker, TokenRole.Marker, node: node);
                    return urlClose + 1 - i;
                }
            }
        }

        return 0;
    }

    private int TryLinkOrWiki(int i, int to)
    {
        // [[target]] wiki-link
        if (i + 1 < to && _text[i + 1] == '[')
        {
            var close = IndexOf("]]", i + 2, to);
            if (close > i + 2)
            {
                var node = _nextNode++;
                FlushText(i);
                Emit(i, 2, TokenType.Marker, TokenRole.Marker, node: node);
                var target = _text.Substring(i + 2, close - (i + 2));
                Emit(i + 2, close - (i + 2), TokenType.WikiLink, TokenRole.Content, target, node);
                Emit(close, 2, TokenType.Marker, TokenRole.Marker, node: node);
                return close + 2 - i;
            }

            return 0;
        }

        // [text](url) link
        var textClose = MatchBracket(i, to);
        if (textClose > 0 && textClose + 1 < to && _text[textClose + 1] == '(')
        {
            var urlClose = MatchParen(textClose + 1, to);
            if (urlClose > 0)
            {
                var node = _nextNode++;
                var textStart = i + 1;
                var (urlStart, urlLen) = Destination(textClose + 2, urlClose);
                FlushText(i);
                Emit(i, 1, TokenType.Marker, TokenRole.Marker, node: node); // [
                Run(textStart, textClose); // recurse into link text, sharing this tokenizer's node counter
                Emit(textClose, 2, TokenType.Marker, TokenRole.Marker, node: node); // ](
                var url = _text.Substring(urlStart, urlLen);
                Emit(urlStart, urlLen, TokenType.LinkUrl, TokenRole.Marker, url, node);
                // Closing marker covers any title (e.g. "title") plus the ')'.
                Emit(urlStart + urlLen, urlClose + 1 - (urlStart + urlLen), TokenType.Marker, TokenRole.Marker, node: node);
                return urlClose + 1 - i;
            }
        }

        return 0;
    }

    private int TryAutolink(int i, int to)
    {
        var close = _text.IndexOf('>', i + 1);
        if (close < 0 || close >= to)
        {
            return 0;
        }

        var inner = _text.Substring(i + 1, close - (i + 1));
        if (inner.Length == 0 || inner.Contains(' ') || !LooksLikeUri(inner))
        {
            return 0;
        }

        var node = _nextNode++;
        FlushText(i);
        Emit(i, 1, TokenType.Marker, TokenRole.Marker, node: node);
        Emit(i + 1, close - (i + 1), TokenType.Autolink, TokenRole.Content, inner, node);
        Emit(close, 1, TokenType.Marker, TokenRole.Marker, node: node);
        return close + 1 - i;
    }

    private int TryBareUrl(int i, int to)
    {
        if (!MatchesAt(i, "http://") && !MatchesAt(i, "https://"))
        {
            return 0;
        }

        // Only treat as autolink at a word boundary.
        if (i > 0 && (char.IsLetterOrDigit(_text[i - 1]) || _text[i - 1] == '/'))
        {
            return 0;
        }

        var j = i;
        while (j < to && !char.IsWhiteSpace(_text[j]) && _text[j] != '<' && _text[j] != ')')
        {
            j++;
        }

        // Trim trailing sentence punctuation.
        while (j > i && IsTrailingUrlPunct(_text[j - 1]))
        {
            j--;
        }

        if (j - i < 11)
        {
            return 0;
        }

        FlushText(i);
        var url = _text.Substring(i, j - i);
        Emit(i, j - i, TokenType.Autolink, TokenRole.Content, url);
        return j - i;
    }

    private int TryEmphasis(int i, int to, char c)
    {
        var run = RunLength(i, to, c);
        // Try strong-emphasis (3), strong (2), emphasis (1), in that order.
        var candidates = run >= 3
            ? new[] { (3, TokenType.StrongEmphasis), (2, TokenType.Strong), (1, TokenType.Emphasis) }
            : run == 2
                ? new[] { (2, TokenType.Strong), (1, TokenType.Emphasis) }
                : new[] { (1, TokenType.Emphasis) };

        foreach (var (count, type) in candidates)
        {
            var n = TryDelimited(i, to, c, count, count, type);
            if (n > 0)
            {
                return n;
            }
        }

        return 0;
    }

    /// <summary>Match a paired delimiter run of <paramref name="c"/> of length <paramref name="count"/>.</summary>
    private int TryDelimited(int i, int to, char c, int minRun, int count, TokenType type)
    {
        var run = RunLength(i, to, c);
        if (run < minRun)
        {
            return 0;
        }

        var contentStart = i + count;
        if (contentStart >= to || _text[contentStart] == c || char.IsWhiteSpace(_text[contentStart]))
        {
            return 0; // no leading space inside; no empty span
        }

        // Underscore (unlike '*') does not delimit intra-word, so snake_case / dunders stay literal: the
        // opening run must not sit immediately after a word character (CommonMark's left-flanking rule for '_').
        if (c == '_' && i > 0 && char.IsLetterOrDigit(_text[i - 1]))
        {
            return 0;
        }

        var j = contentStart;
        while (j < to)
        {
            // A delimiter escaped by a backslash (an odd run of '\' before it) is a literal, not a closer.
            if (_text[j] == c && !char.IsWhiteSpace(_text[j - 1]) && !IsEscaped(j))
            {
                var closeRun = RunLength(j, to, c);
                // The '_' closer is likewise blocked when it sits immediately before a word character.
                var intraWord = c == '_' && j + closeRun < to && char.IsLetterOrDigit(_text[j + closeRun]);
                if (closeRun >= count && !intraWord)
                {
                    var node = _nextNode++;
                    FlushText(i);
                    Emit(i, count, TokenType.Marker, TokenRole.Marker, node: node);
                    Emit(contentStart, j - contentStart, type, TokenRole.Content, node: node);
                    Emit(j, count, TokenType.Marker, TokenRole.Marker, node: node);
                    return j + count - i;
                }

                j += closeRun;
            }
            else
            {
                j++;
            }
        }

        return 0;
    }

    /// <summary>True when the character at <paramref name="pos"/> is backslash-escaped (preceded by an odd run of '\').</summary>
    private bool IsEscaped(int pos)
    {
        var slashes = 0;
        for (var k = pos - 1; k >= 0 && _text[k] == '\\'; k--)
        {
            slashes++;
        }

        return (slashes & 1) == 1;
    }

    // ----- token emission -----

    private void AppendLiteral(int i)
    {
        if (_pendingTextStart < 0)
        {
            _pendingTextStart = i;
        }
    }

    private void FlushText(int end)
    {
        if (_pendingTextStart >= 0 && end > _pendingTextStart)
        {
            Emit(_pendingTextStart, end - _pendingTextStart, TokenType.Text, TokenRole.Content);
        }

        _pendingTextStart = -1;
    }

    private void Emit(int start, int length, TokenType type, TokenRole role, string? meta = null, int node = 0)
    {
        if (length <= 0)
        {
            return;
        }

        _out.Add(new Token(_absStart + start, length, type, role, meta, node));
    }

    // ----- scanning helpers -----

    /// <summary>
    /// The URL destination span inside a link/image's parentheses <c>[from, to)</c>: leading
    /// whitespace is skipped and the destination ends at the first whitespace (an unbracketed
    /// CommonMark destination can't contain spaces), so an optional <c>"title"</c> after it is
    /// excluded from the URL rather than corrupting it.
    /// </summary>
    private (int Start, int Length) Destination(int from, int to)
    {
        var s = from;
        while (s < to && char.IsWhiteSpace(_text[s]))
        {
            s++;
        }

        var e = s;
        while (e < to && !char.IsWhiteSpace(_text[e]))
        {
            e++;
        }

        return (s, e - s);
    }

    private int RunLength(int i, int to, char c)
    {
        var n = 0;
        while (i + n < to && _text[i + n] == c)
        {
            n++;
        }

        return n;
    }

    private int IndexOf(string needle, int from, int to)
    {
        var idx = _text.IndexOf(needle, from, StringComparison.Ordinal);
        return idx >= 0 && idx + needle.Length <= to ? idx : -1;
    }

    private bool MatchesAt(int i, string s) =>
        i + s.Length <= _text.Length && string.CompareOrdinal(_text, i, s, 0, s.Length) == 0;

    /// <summary>Index of the <c>]</c> that closes the <c>[</c> at <paramref name="open"/>, honoring nesting; or -1.</summary>
    private int MatchBracket(int open, int to)
    {
        var depth = 0;
        for (var k = open; k < to; k++)
        {
            var ch = _text[k];
            if (ch == '\\')
            {
                k++;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return k;
                }
            }
        }

        return -1;
    }

    /// <summary>Index of the <c>)</c> that closes the <c>(</c> at <paramref name="open"/>, honoring nesting; or -1.</summary>
    private int MatchParen(int open, int to)
    {
        var depth = 0;
        for (var k = open; k < to; k++)
        {
            var ch = _text[k];
            if (ch == '\\')
            {
                k++;
                continue;
            }

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return k;
                }
            }
        }

        return -1;
    }

    private static bool LooksLikeUri(string s)
    {
        var colon = s.IndexOf(':');
        if (colon <= 0)
        {
            return s.Contains('@') && s.Contains('.'); // bare email autolink
        }

        for (var k = 0; k < colon; k++)
        {
            if (!char.IsLetterOrDigit(s[k]) && s[k] is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiPunctuation(char c) =>
        c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    private static bool IsTrailingUrlPunct(char c) => c is '.' or ',' or ';' or ':' or '!' or '?' or ')';
}
