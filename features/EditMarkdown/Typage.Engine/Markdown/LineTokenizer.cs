using System.Globalization;

namespace Typage.Engine.Markdown;

/// <summary>
/// Tokenizes a single line given the block state it is entered with, and returns the block
/// state the next line is entered with. This is the heart of the incremental, line-based
/// tokenizer (PRD §8): no full-document AST, just a per-line classifier driven by carried
/// block state. Table recognition needs neighbour context and lives one level up in
/// <see cref="MarkdownModel"/>; everything else is decided here.
/// </summary>
internal static class LineTokenizer
{
    /// <summary>
    /// Tokenize <paramref name="line"/> (whose first char is at absolute <paramref name="lineStart"/>),
    /// appending tokens to <paramref name="output"/>. <paramref name="lineIndex"/> gates front-matter,
    /// which may only open on the very first line. Returns the state for the next line.
    /// </summary>
    public static BlockState Tokenize(
        string line, int lineStart, int lineIndex, BlockState entering, List<Token> output,
        bool allowFrontMatterOpen = true)
    {
        // Content length excludes a trailing '\r' so CRLF files tokenize like LF ones.
        var contentLen = line.Length;
        if (contentLen > 0 && line[contentLen - 1] == '\r')
        {
            contentLen--;
        }

        return entering.Kind switch
        {
            BlockKind.FrontMatter => TokenizeInFrontMatter(line, lineStart, contentLen, output),
            BlockKind.FencedCode => TokenizeInFencedCode(line, lineStart, contentLen, entering, output),
            BlockKind.IndentedCode => TokenizeInIndentedCode(line, lineStart, contentLen, lineIndex, allowFrontMatterOpen, output),
            _ => TokenizeNormal(line, lineStart, contentLen, lineIndex, allowFrontMatterOpen, entering.AfterBlank, output),
        };
    }

    /// <summary>
    /// Inside an indented (4-space) code block: a line indented &gt;= 4 (or a blank line) stays in the
    /// block; the first <b>non-blank</b> line dedented below 4 ends it and is re-classified as a normal
    /// line. A blank line — empty <i>or</i> whitespace-only, whatever its width — keeps the block open so a
    /// following indented line continues the same block (CommonMark §4.4); it isn't a dedent.
    /// </summary>
    private static BlockState TokenizeInIndentedCode(
        string line, int lineStart, int len, int lineIndex, bool allowFrontMatterOpen, List<Token> output)
    {
        var ws = LeadingWhitespace(line, len, out var columns);
        if (ws == len)
        {
            return BlockState.IndentedCode; // a blank line (any indent) is interior; stay open, emit no token
        }

        if (columns >= 4)
        {
            output.Add(new Token(lineStart, len, TokenType.CodeText, TokenRole.Content));
            return BlockState.IndentedCode;
        }

        // Dedented to a real (non-blank) line — the block has ended; classify this line normally.
        return TokenizeNormal(line, lineStart, len, lineIndex, allowFrontMatterOpen, afterBlank: false, output);
    }

    private static BlockState TokenizeInFrontMatter(string line, int lineStart, int len, List<Token> output)
    {
        if (IsFrontMatterFence(line, len))
        {
            output.Add(new Token(lineStart, len, TokenType.FrontMatterFence, TokenRole.Marker));
            return BlockState.Normal;
        }

        if (len > 0)
        {
            output.Add(new Token(lineStart, len, TokenType.FrontMatterText, TokenRole.Content));
        }

        return BlockState.FrontMatter;
    }

    private static BlockState TokenizeInFencedCode(string line, int lineStart, int len, BlockState entering, List<Token> output)
    {
        if (IsClosingFence(line, len, entering.FenceChar, entering.FenceLength))
        {
            output.Add(new Token(lineStart, len, TokenType.CodeFence, TokenRole.Marker));
            return BlockState.Normal;
        }

        if (len > 0)
        {
            output.Add(new Token(lineStart, len, TokenType.CodeText, TokenRole.Content));
        }

        return entering; // still inside the fence (unterminated fences extend to EOF — PRD §7.4)
    }

    private static BlockState TokenizeNormal(string line, int lineStart, int len, int lineIndex, bool allowFrontMatterOpen, bool afterBlank, List<Token> output)
    {
        // Both spaces and tabs are indentation. `indent` is the char count of the leading whitespace (used
        // for marker positions), `indentCols` its width in columns (used for the CommonMark <=3 / >=4 rules).
        var indent = LeadingWhitespace(line, len, out var indentCols);

        // Front matter opens only as the very first line, and only when a closing fence exists
        // below it (otherwise a lone "---" is a horizontal rule — decided in MarkdownModel).
        if (lineIndex == 0 && allowFrontMatterOpen && IsFrontMatterFence(line, len))
        {
            output.Add(new Token(lineStart, len, TokenType.FrontMatterFence, TokenRole.Marker));
            return BlockState.FrontMatter;
        }

        // Indented code block: a non-blank line indented >= 4 columns at a fresh block position (after a
        // blank line or the top of the document) opens one. It cannot interrupt a paragraph, hence the
        // afterBlank gate (CommonMark §4.4). A single leading tab is 4 columns, so it qualifies too.
        if (afterBlank && indentCols >= 4 && len > indent)
        {
            output.Add(new Token(lineStart, len, TokenType.CodeText, TokenRole.Content));
            return BlockState.IndentedCode;
        }

        // Fenced code opens with >=3 backticks or tildes (indent <= 3 columns).
        if (indentCols <= 3 && TryFenceOpen(line, indent, len, out var fenceChar, out var fenceLen, out var lang))
        {
            output.Add(new Token(lineStart, len, TokenType.CodeFence, TokenRole.Marker, lang));
            return BlockState.Fenced(fenceChar, fenceLen, lang);
        }

        // Horizontal rule.
        if (indentCols <= 3 && IsHorizontalRule(line, indent, len))
        {
            output.Add(new Token(lineStart, len, TokenType.HorizontalRule, TokenRole.Marker));
            return BlockState.Normal;
        }

        // ATX heading.
        if (indentCols <= 3 && TryHeading(line, indent, len, out var hashCount, out var afterMarker))
        {
            output.Add(new Token(lineStart + indent, afterMarker - indent, TokenType.HeadingMarker, TokenRole.Marker, hashCount.ToString()));
            InlineTokenizer.Tokenize(line, lineStart, afterMarker, len, output);
            return BlockState.Normal;
        }

        // Blockquote: consume one or more '>' prefixes, then inline-tokenize the remainder. The marker spans the
        // whole prefix but carries its *depth* — the number of '>' — in Meta, so the surface can express nesting
        // (a bar per level) instead of drawing one bar however deeply the quote is nested.
        if (indentCols <= 3 && afterMarkerIsQuote(line, indent, len))
        {
            var p = indent;
            var depth = 0;
            while (p < len && (line[p] == '>' || line[p] == ' ' || line[p] == '\t'))
            {
                if (line[p] == '>')
                {
                    depth++;
                }

                p++;
            }

            output.Add(new Token(
                lineStart + indent, p - indent, TokenType.BlockquoteMarker, TokenRole.Marker,
                depth.ToString(CultureInfo.InvariantCulture)));
            InlineTokenizer.Tokenize(line, lineStart, p, len, output, detectHardBreak: true);
            return BlockState.Normal;
        }

        // List item (unordered / ordered), with optional task marker.
        if (TryListMarker(line, indent, len, out var markerEnd))
        {
            output.Add(new Token(lineStart + indent, markerEnd - indent, TokenType.ListMarker, TokenRole.Marker));
            var contentStart = markerEnd;
            if (TryTaskMarker(line, contentStart, len, out var taskEnd, out var state))
            {
                // The marker carries the *state* it spells in Meta (like a blockquote's depth), so a consumer
                // reads the six-state vocabulary off the token instead of re-parsing the box character.
                output.Add(new Token(
                    lineStart + contentStart, taskEnd - contentStart, TokenType.TaskMarker, TokenRole.Marker,
                    TaskStates.MarkText(state)));
                contentStart = taskEnd;
            }

            InlineTokenizer.Tokenize(line, lineStart, contentStart, len, output, detectHardBreak: true);
            return BlockState.Normal;
        }

        // Plain paragraph line -> inline only. A blank line — empty or whitespace-only (indent spans the
        // whole line) — marks the next line as a fresh block position where an indented code block may open,
        // so an indented line following a "blank" line of spaces still starts/continues a code block.
        InlineTokenizer.Tokenize(line, lineStart, 0, len, output, detectHardBreak: true);
        return BlockState.NormalAfter(indent == len);
    }

    // ----- setext headings (driven from MarkdownModel, which owns the one-line lookahead) -----

    /// <summary>The content length of <paramref name="line"/>, excluding a trailing CR (so CRLF files agree with LF ones).</summary>
    public static int ContentLength(string line) =>
        line.Length > 0 && line[^1] == '\r' ? line.Length - 1 : line.Length;

    /// <summary>
    /// Whether <paramref name="line"/> is a setext underline: a run of <c>=</c> (level 1) or <c>-</c> (level 2)
    /// and nothing else, indented at most 3 columns, with optional trailing whitespace.
    /// </summary>
    public static bool IsSetextUnderline(string line, out int level)
    {
        level = 0;
        var len = ContentLength(line);
        var indent = LeadingWhitespace(line, len, out var indentCols);
        if (indentCols > 3 || indent >= len)
        {
            return false;
        }

        var c = line[indent];
        if (c is not ('=' or '-'))
        {
            return false;
        }

        var p = indent;
        while (p < len && line[p] == c)
        {
            p++;
        }

        // Only trailing whitespace may follow the run — "--- x" is not an underline.
        while (p < len && (line[p] == ' ' || line[p] == '\t'))
        {
            p++;
        }

        if (p != len)
        {
            return false;
        }

        level = c == '=' ? 1 : 2;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="line"/> can be the <i>text</i> of a setext heading: an ordinary paragraph line.
    /// Anything that already opens a block of its own — an ATX heading, a rule, a quote, a list item, a fence,
    /// front matter, or an indented-code-depth line — is not paragraph text and takes no underline.
    /// </summary>
    /// <param name="isFrontMatterOpen">True when this is line 0 of a document whose front matter opens here;
    /// that <c>---</c> is a fence, not the text of a heading.</param>
    public static bool IsSetextTextLine(string line, bool isFrontMatterOpen)
    {
        if (isFrontMatterOpen)
        {
            return false;
        }

        var len = ContentLength(line);
        var indent = LeadingWhitespace(line, len, out var indentCols);
        if (indent == len || indentCols > 3)
        {
            return false; // blank, or indented far enough to be code
        }

        return !IsHorizontalRule(line, indent, len)
               && !TryHeading(line, indent, len, out _, out _)
               && !afterMarkerIsQuote(line, indent, len)
               && !TryListMarker(line, indent, len, out _)
               && !TryFenceOpen(line, indent, len, out _, out _, out _)
               && !IsFrontMatterFence(line, len);
    }

    /// <summary>Inline-tokenize a whole line's content (the setext path — the heading's text has no line prefix).</summary>
    public static void TokenizeInline(string line, int lineStart, List<Token> output) =>
        InlineTokenizer.Tokenize(line, lineStart, 0, ContentLength(line), output);

    // ----- recognizers -----

    /// <summary>
    /// The number of leading whitespace characters (spaces and tabs) on the line; <paramref name="columns"/>
    /// returns their width in columns, a tab advancing to the next 4-column tab stop (CommonMark). Both
    /// spaces and tabs count as indentation.
    /// </summary>
    private static int LeadingWhitespace(string line, int len, out int columns)
    {
        var n = 0;
        columns = 0;
        while (n < len && (line[n] == ' ' || line[n] == '\t'))
        {
            columns += line[n] == '\t' ? 4 - (columns % 4) : 1;
            n++;
        }

        return n;
    }

    private static bool IsFrontMatterFence(string line, int len)
    {
        var t = line.AsSpan(0, len).TrimEnd();
        return t.Length == 3 && (t.SequenceEqual("---") || t.SequenceEqual("...") || t.SequenceEqual("+++"));
    }

    /// <summary>True when the whole line is a front-matter delimiter (<c>---</c>/<c>...</c>/<c>+++</c>).</summary>
    internal static bool IsFrontMatterFenceLine(string line)
    {
        var len = line.Length;
        if (len > 0 && line[len - 1] == '\r')
        {
            len--;
        }

        return IsFrontMatterFence(line, len);
    }

    private static bool TryFenceOpen(string line, int indent, int len, out char fenceChar, out int fenceLen, out string? lang)
    {
        fenceChar = '\0';
        fenceLen = 0;
        lang = null;
        if (indent >= len)
        {
            return false;
        }

        var c = line[indent];
        if (c != '`' && c != '~')
        {
            return false;
        }

        var run = 0;
        while (indent + run < len && line[indent + run] == c)
        {
            run++;
        }

        if (run < 3)
        {
            return false;
        }

        fenceChar = c;
        fenceLen = run;
        var info = line.AsSpan(indent + run, len - (indent + run)).Trim();
        // Backtick info strings may not contain a backtick (CommonMark).
        if (c == '`' && info.Contains('`'))
        {
            return false;
        }

        lang = info.Length == 0 ? null : info.ToString();
        return true;
    }

    private static bool IsClosingFence(string line, int len, char fenceChar, int fenceLen)
    {
        var indent = LeadingWhitespace(line, len, out var columns);
        if (columns > 3)
        {
            return false;
        }

        var run = 0;
        while (indent + run < len && line[indent + run] == fenceChar)
        {
            run++;
        }

        if (run < fenceLen)
        {
            return false;
        }

        return line.AsSpan(indent + run, len - (indent + run)).Trim().Length == 0;
    }

    private static bool IsHorizontalRule(string line, int indent, int len)
    {
        if (indent >= len)
        {
            return false;
        }

        var c = line[indent];
        if (c != '-' && c != '*' && c != '_')
        {
            return false;
        }

        var count = 0;
        for (var k = indent; k < len; k++)
        {
            var ch = line[k];
            if (ch == c)
            {
                count++;
            }
            else if (ch != ' ')
            {
                return false;
            }
        }

        return count >= 3;
    }

    private static bool TryHeading(string line, int indent, int len, out int hashCount, out int afterMarker)
    {
        hashCount = 0;
        afterMarker = indent;
        var k = indent;
        while (k < len && line[k] == '#')
        {
            k++;
        }

        hashCount = k - indent;
        if (hashCount is < 1 or > 6)
        {
            return false;
        }

        // Must be followed by a space or end of line.
        if (k < len && line[k] != ' ')
        {
            return false;
        }

        if (k < len && line[k] == ' ')
        {
            k++;
        }

        afterMarker = k;
        return true;
    }

    private static bool afterMarkerIsQuote(string line, int indent, int len) =>
        indent < len && line[indent] == '>';

    private static bool TryListMarker(string line, int indent, int len, out int markerEnd)
    {
        markerEnd = indent;
        if (indent >= len)
        {
            return false;
        }

        var c = line[indent];
        if (c is '-' or '*' or '+')
        {
            // Need a following space (and not a horizontal rule, already handled).
            if (indent + 1 < len && line[indent + 1] == ' ')
            {
                markerEnd = indent + 2;
                return true;
            }

            return false;
        }

        // Ordered: one or more digits then '.' or ')' then a space.
        var k = indent;
        var digits = 0;
        while (k < len && char.IsAsciiDigit(line[k]) && digits < 9)
        {
            k++;
            digits++;
        }

        if (digits == 0 || k >= len)
        {
            return false;
        }

        if ((line[k] == '.' || line[k] == ')') && k + 1 < len && line[k + 1] == ' ')
        {
            markerEnd = k + 2;
            return true;
        }

        return false;
    }

    private static bool TryTaskMarker(string line, int start, int len, out int taskEnd, out TaskState state)
    {
        taskEnd = start;
        state = TaskState.NotStarted;

        // A box holds one character of the task-state vocabulary — "[ ]", "[/]", "[?]", "[>]", "[x]", "[-]" —
        // followed by a space (or EOL). Any other character is not a box: the brackets stay literal text.
        if (start + 2 < len && line[start] == '[' && line[start + 2] == ']' && TaskStates.IsMark(line[start + 1]))
        {
            var after = start + 3;
            if (after >= len || line[after] == ' ')
            {
                state = TaskStates.FromMark(line[start + 1]);
                taskEnd = after < len ? after + 1 : after;
                return true;
            }
        }

        return false;
    }
}
