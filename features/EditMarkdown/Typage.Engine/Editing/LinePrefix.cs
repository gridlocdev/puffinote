namespace Typage.Engine.Editing;

/// <summary>The kind of list a line's marker belongs to (or none).</summary>
public enum ListKind
{
    None,
    Unordered,
    Ordered,
}

/// <summary>What an Enter keypress should do on the caret's line (PRD §7.2 editing assists).</summary>
public enum EnterKind
{
    /// <summary>Insert a plain newline.</summary>
    Plain,

    /// <summary>Insert a newline followed by a continuation prefix (next list item / quote / indent).</summary>
    Continue,

    /// <summary>The line is an empty list/quote item — strip its marker instead of adding another.</summary>
    Exit,
}

/// <summary>
/// The decision for an Enter keypress. <see cref="Text"/> is what to insert at the caret (for
/// <see cref="EnterKind.Continue"/>); for <see cref="EnterKind.Exit"/> the marker span
/// <c>[ExitStart, ExitStart+ExitLength)</c> (columns within the line) is deleted and nothing is
/// inserted, so an empty item clears back to its outer level.
/// </summary>
public readonly record struct EnterResult(EnterKind Kind, string Text, int ExitStart, int ExitLength);

/// <summary>
/// The parsed leading block structure of a single line: indentation, an optional blockquote
/// prefix, and an optional list marker with task checkbox. Pure and headless — the editing
/// assists (Enter continuation/exit, Tab indent/outdent, list renumbering; PRD §7.2) are decided
/// here and exercised by the engine tests without a window. Mirrors the recognizers in
/// <see cref="Markdown.LineTokenizer"/> so the assists agree with what the tokenizer styles.
/// </summary>
public readonly record struct LinePrefix(
    int Indent,
    string Quote,
    ListKind List,
    char Bullet,
    int Number,
    char Delim,
    int DigitCount,
    bool HasTask,
    bool TaskChecked,
    int MarkerStart,
    int Length,
    string IndentText = "")
{
    /// <summary>True when the line carries a list marker.</summary>
    public bool HasList => List != ListKind.None;

    /// <summary>True when the line carries a blockquote prefix.</summary>
    public bool HasQuote => Quote.Length > 0;

    /// <summary>Parse the leading structure of <paramref name="line"/> (a single line, trailing CR tolerated).</summary>
    public static LinePrefix Parse(string line)
    {
        var len = line.Length;
        if (len > 0 && line[len - 1] == '\r')
        {
            len--;
        }

        // Leading indentation is any run of spaces and tabs (both count). Indent is its char length;
        // IndentText is the literal whitespace so a continued line reproduces the same tabs/spaces.
        var i = 0;
        while (i < len && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }

        var indent = i;
        var indentText = line.Substring(0, i);

        // Blockquote: one or more '>' each optionally followed by spaces/tabs, captured verbatim.
        var quoteStart = i;
        while (i < len && line[i] == '>')
        {
            i++;
            while (i < len && (line[i] == ' ' || line[i] == '\t'))
            {
                i++;
            }
        }

        var quote = line.Substring(quoteStart, i - quoteStart);
        var markerStart = i;

        var kind = ListKind.None;
        var bullet = '\0';
        var number = 0;
        var digitCount = 0;
        var delim = '\0';
        var afterMarker = i;

        if (i < len)
        {
            var c = line[i];
            if (c is '-' or '*' or '+' && i + 1 < len && line[i + 1] == ' ')
            {
                kind = ListKind.Unordered;
                bullet = c;
                afterMarker = i + 2;
            }
            else if (char.IsAsciiDigit(c))
            {
                var k = i;
                while (k < len && char.IsAsciiDigit(line[k]) && k - i < 9)
                {
                    k++;
                }

                if (k > i && k < len && line[k] is '.' or ')' && k + 1 < len && line[k + 1] == ' ')
                {
                    kind = ListKind.Ordered;
                    digitCount = k - i;
                    number = int.Parse(line.AsSpan(i, digitCount));
                    delim = line[k];
                    afterMarker = k + 2;
                }
            }
        }

        var hasTask = false;
        var taskChecked = false;
        var prefixEnd = kind == ListKind.None ? markerStart : afterMarker;

        if (kind != ListKind.None && afterMarker + 2 < len && line[afterMarker] == '['
            && line[afterMarker + 2] == ']' && line[afterMarker + 1] is ' ' or 'x' or 'X')
        {
            var after = afterMarker + 3;
            if (after >= len || line[after] == ' ')
            {
                hasTask = true;
                taskChecked = line[afterMarker + 1] is 'x' or 'X';
                prefixEnd = after < len ? after + 1 : after;
            }
        }

        return new LinePrefix(indent, quote, kind, bullet, number, delim, digitCount, hasTask, taskChecked, markerStart, prefixEnd, indentText);
    }

    /// <summary>The prefix to begin the next sibling item/line with (after a newline) — PRD §7.2.</summary>
    public string ContinuationPrefix()
    {
        var prefix = IndentText + Quote;
        prefix += List switch
        {
            ListKind.Unordered => $"{Bullet} ",
            ListKind.Ordered => $"{Number + 1}{Delim} ",
            _ => string.Empty,
        };

        if (HasTask)
        {
            prefix += "[ ] "; // a continued task item starts unchecked
        }

        return prefix;
    }

    /// <summary>
    /// Decide what Enter should do on <paramref name="line"/> with the caret at
    /// <paramref name="caretColumn"/>. Continues a list/blockquote, exits an empty item, auto-indents
    /// a plain indented line, or falls back to a plain newline.
    /// </summary>
    public static EnterResult ComputeEnter(string line, int caretColumn)
    {
        var contentLen = line.Length;
        if (contentLen > 0 && line[contentLen - 1] == '\r')
        {
            contentLen--;
        }

        var p = Parse(line);

        if (!p.HasList && !p.HasQuote)
        {
            // Auto-indent: carry the leading whitespace (tabs/spaces, verbatim) down, but only what
            // precedes the caret.
            var keep = Math.Min(p.IndentText.Length, caretColumn);
            var carried = p.IndentText[..Math.Max(0, keep)];
            return carried.Length > 0
                ? new EnterResult(EnterKind.Continue, "\n" + carried, 0, 0)
                : new EnterResult(EnterKind.Plain, "\n", 0, 0);
        }

        // Empty item (only the prefix, optionally trailing spaces) with the caret at/after the marker:
        // pressing Enter exits the block rather than spawning another empty item.
        var afterPrefix = contentLen > p.Length ? line.AsSpan(p.Length, contentLen - p.Length) : default;
        var isEmpty = afterPrefix.Trim().Length == 0;
        if (isEmpty && caretColumn >= p.MarkerStart)
        {
            // Drop the list marker (keeping any outer indent + quote); or, for a bare quote, the quote.
            var exitStart = p.HasList ? p.MarkerStart : p.Indent;
            return new EnterResult(EnterKind.Exit, string.Empty, exitStart, contentLen - exitStart);
        }

        return new EnterResult(EnterKind.Continue, "\n" + p.ContinuationPrefix(), 0, 0);
    }
}
