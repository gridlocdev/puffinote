using System.Globalization;
using Typage.Engine.Document;

namespace Typage.Engine.Markdown;

/// <summary>
/// The tokenized view of a document: per-line tokens plus the block-state checkpoint cache
/// (PRD §8.1, §8.2). Every line records the <see cref="BlockState"/> it was entered with, so
/// rendering any viewport can resume from the nearest line above instead of from the top of
/// the file, and an edit re-tokenizes only from its first affected line downward
/// (<see cref="Update"/>) — reusing the untouched prefix.
/// </summary>
public sealed class MarkdownModel
{
    private readonly BlockState[] _entering;       // checkpoint cache: state entering each line
    private readonly List<Token>[] _lineTokens;    // tokens for each line (absolute offsets)

    private MarkdownModel(BlockState[] entering, List<Token>[] lineTokens)
    {
        _entering = entering;
        _lineTokens = lineTokens;
    }

    /// <summary>Number of lines covered.</summary>
    public int LineCount => _lineTokens.Length;

    /// <summary>The block state a line is entered with (the checkpoint above it).</summary>
    public BlockState EnteringState(int line) => _entering[line];

    /// <summary>The tokens of <paramref name="line"/>, in source order.</summary>
    public IReadOnlyList<Token> LineTokens(int line) => _lineTokens[line];

    /// <summary>All tokens in document order (handy for tests, the CLI, and the HTML renderer).</summary>
    public IEnumerable<Token> AllTokens()
    {
        foreach (var line in _lineTokens)
        {
            foreach (var t in line)
            {
                yield return t;
            }
        }
    }

    /// <summary>Tokenize a whole document from scratch.</summary>
    public static MarkdownModel Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var n = document.LineCount;
        var entering = new BlockState[n];
        var tokens = new List<Token>[n];
        TokenizeRange(document, 0, n, BlockState.Start, entering, tokens);
        return new MarkdownModel(entering, tokens);
    }

    /// <summary>
    /// Incrementally re-tokenize after an edit: reuse the untouched prefix
    /// <c>[0, firstChangedLine)</c> and recompute the rest. Worst case is O(n) — e.g. typing an
    /// opening fence at the top flips the file into code — exactly as the PRD allows (§8.2); the
    /// common edit only re-runs a short tail. <paramref name="dirtyFromLine"/> reports the first
    /// re-tokenized line so the renderer can refresh just the affected region.
    /// </summary>
    public MarkdownModel Update(TextDocument newDocument, int firstChangedLine, out int dirtyFromLine)
    {
        ArgumentNullException.ThrowIfNull(newDocument);
        var n = newDocument.LineCount;
        firstChangedLine = Math.Clamp(firstChangedLine, 0, Math.Max(0, n - 1));

        // A table body row's classification depends on the header/delimiter above it, which the
        // carried BlockState doesn't track. Back up to the start of any contiguous run of pipe
        // rows so the whole table is re-evaluated as a unit (PRD §8.3 table recognition).
        while (firstChangedLine > 0 && IsPotentialTableRow(newDocument.GetLineText(firstChangedLine - 1)))
        {
            firstChangedLine--;
        }

        // A setext heading reaches *backwards*: typing (or deleting) the "===" under a paragraph is what makes
        // — or unmakes — the line above it a heading. The carried BlockState can't see that, so back up one
        // line whenever the line above is ordinary paragraph text, and let it be re-classified with the
        // lookahead. Without this, deleting an underline would leave the heading above it stranded.
        if (firstChangedLine > 0 && LineTokenizer.IsSetextTextLine(newDocument.GetLineText(firstChangedLine - 1), isFrontMatterOpen: false))
        {
            firstChangedLine--;
        }

        // Never resume *inside* a setext pair. A bare "===" is, on its own, an ordinary paragraph line — so the
        // back-up above can land on an underline whose heading text sits above it. Starting there would tokenize
        // the underline as a paragraph, because the lookahead that binds it to its text line only runs from the
        // text line. Step back onto the pair's first line instead.
        while (firstChangedLine > 0
               && LineTokenizer.IsSetextUnderline(newDocument.GetLineText(firstChangedLine), out _)
               && LineTokenizer.IsSetextTextLine(newDocument.GetLineText(firstChangedLine - 1), isFrontMatterOpen: false))
        {
            firstChangedLine--;
        }

        // The entering state at firstChangedLine is unchanged: every line above it is identical.
        var startState = firstChangedLine < _entering.Length ? _entering[firstChangedLine] : BlockState.Normal;

        var entering = new BlockState[n];
        var tokens = new List<Token>[n];
        Array.Copy(_entering, entering, Math.Min(firstChangedLine, _entering.Length));
        Array.Copy(_lineTokens, tokens, Math.Min(firstChangedLine, _lineTokens.Length));

        TokenizeRange(newDocument, firstChangedLine, n, startState, entering, tokens);
        dirtyFromLine = firstChangedLine;
        return new MarkdownModel(entering, tokens);
    }

    /// <summary>
    /// Tokenize lines <c>[fromLine, toLine)</c>, filling <paramref name="entering"/> and
    /// <paramref name="tokens"/>. Handles the multi-line carried block state and the table
    /// lookahead (a header line immediately followed by a delimiter row — PRD §8.3).
    /// </summary>
    private static void TokenizeRange(
        TextDocument doc, int fromLine, int toLine, BlockState startState,
        BlockState[] entering, List<Token>[] tokens)
    {
        // Front matter opens at line 0 only when a matching closing fence exists below it.
        var allowFrontMatter = doc.LineCount >= 2
            && LineTokenizer.IsFrontMatterFenceLine(doc.GetLineText(0))
            && HasClosingFrontMatterFence(doc);

        var state = startState;
        var i = fromLine;
        while (i < toLine)
        {
            entering[i] = state;

            if (state.Kind == BlockKind.Normal
                && i + 1 < doc.LineCount
                && IsPotentialTableRow(doc.GetLineText(i))
                && IsTableDelimiterRow(doc.GetLineText(i + 1)))
            {
                i = TokenizeTable(doc, i, toLine, entering, tokens);
                state = BlockState.Normal; // a table row is non-blank; the line after isn't a fresh block position
                continue; // table is self-contained; state stays Normal
            }

            // A setext heading — paragraph text with a "===" (H1) or "---" (H2) underline beneath it. Like a
            // table, it can't be decided a line at a time: the underline is what makes the line above a
            // heading, so it needs the same one-line lookahead. Checking it here, *before* the line below is
            // classified, is also what stops a setext "---" from being taken for a horizontal rule (the
            // tokenizer would otherwise reach its HR rule first, which is exactly what it used to do).
            if (state.Kind == BlockKind.Normal
                && i + 1 < toLine
                && LineTokenizer.IsSetextTextLine(doc.GetLineText(i), allowFrontMatter && i == 0)
                && LineTokenizer.IsSetextUnderline(doc.GetLineText(i + 1), out var setextLevel))
            {
                i = TokenizeSetextHeading(doc, i, setextLevel, entering, tokens);
                state = BlockState.Normal;
                continue;
            }

            var line = doc.GetLineText(i);
            var lineToks = new List<Token>();
            state = LineTokenizer.Tokenize(line, doc.GetLineStart(i), i, state, lineToks, allowFrontMatter);
            tokens[i] = lineToks;
            i++;
        }
    }

    /// <summary>
    /// Tokenize a setext heading: the text line at <paramref name="line"/> and its underline below. Returns the
    /// line index after the pair.
    ///
    /// <para>The text line carries a <b>zero-length</b> <see cref="TokenType.HeadingMarker"/> holding the level,
    /// so it is shaped exactly like an ATX heading — there is simply no <c>#</c> to point at. Every consumer
    /// already reads the level off the line's first token, so none of them needs to know the difference.</para>
    /// </summary>
    private static int TokenizeSetextHeading(
        TextDocument doc, int line, int level, BlockState[] entering, List<Token>[] tokens)
    {
        var start = doc.GetLineStart(line);
        var text = doc.GetLineText(line);
        var headingTokens = new List<Token>
        {
            new(start, 0, TokenType.HeadingMarker, TokenRole.Marker, level.ToString(CultureInfo.InvariantCulture)),
        };
        LineTokenizer.TokenizeInline(text, start, headingTokens);
        entering[line] = BlockState.Normal;
        tokens[line] = headingTokens;

        var underline = line + 1;
        var underlineStart = doc.GetLineStart(underline);
        entering[underline] = BlockState.Normal;
        tokens[underline] =
        [
            new Token(underlineStart, LineTokenizer.ContentLength(doc.GetLineText(underline)), TokenType.SetextUnderline, TokenRole.Marker),
        ];

        return underline + 1;
    }

    /// <summary>Tokenize a contiguous GFM pipe-table block; returns the line index after it.</summary>
    private static int TokenizeTable(TextDocument doc, int start, int toLine, BlockState[] entering, List<Token>[] tokens)
    {
        var i = start;
        // Header row.
        entering[i] = BlockState.Normal;
        tokens[i] = TokenizeTableRow(doc, i, isDelimiter: false);
        i++;
        // Delimiter row.
        if (i < toLine)
        {
            entering[i] = BlockState.Normal;
            tokens[i] = TokenizeTableRow(doc, i, isDelimiter: true);
            i++;
        }

        // Body rows: continue while the line still looks like a table row.
        while (i < toLine && IsPotentialTableRow(doc.GetLineText(i)))
        {
            entering[i] = BlockState.Normal;
            tokens[i] = TokenizeTableRow(doc, i, isDelimiter: false);
            i++;
        }

        return i;
    }

    private static List<Token> TokenizeTableRow(TextDocument doc, int line, bool isDelimiter)
    {
        var text = doc.GetLineText(line);
        var start = doc.GetLineStart(line);
        var len = TrimmedLength(text);
        var output = new List<Token>();

        if (isDelimiter)
        {
            if (len > 0)
            {
                output.Add(new Token(start, len, TokenType.TableDelimiter, TokenRole.Marker));
            }

            return output;
        }

        var cellStart = 0;
        for (var k = 0; k <= len; k++)
        {
            var isPipe = k < len && text[k] == '|' && (k == 0 || text[k - 1] != '\\');
            if (isPipe || k == len)
            {
                if (k > cellStart)
                {
                    InlineTokenizer.Tokenize(text, start, cellStart, k, output);
                }

                if (isPipe)
                {
                    output.Add(new Token(start + k, 1, TokenType.Marker, TokenRole.Marker));
                    cellStart = k + 1;
                }
            }
        }

        return output;
    }

    // ----- table recognition -----

    private static bool HasClosingFrontMatterFence(TextDocument doc)
    {
        for (var i = 1; i < doc.LineCount; i++)
        {
            if (LineTokenizer.IsFrontMatterFenceLine(doc.GetLineText(i)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPotentialTableRow(string line)
    {
        var len = TrimmedLength(line);
        for (var k = 0; k < len; k++)
        {
            if (line[k] == '|' && (k == 0 || line[k - 1] != '\\'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTableDelimiterRow(string line)
    {
        var len = TrimmedLength(line);
        var sawDash = false;
        var sawContent = false;
        for (var k = 0; k < len; k++)
        {
            var c = line[k];
            switch (c)
            {
                case '-':
                    sawDash = true;
                    sawContent = true;
                    break;
                case '|' or ':' or ' ':
                    sawContent = sawContent || c is '|' or ':';
                    break;
                default:
                    return false;
            }
        }

        return sawDash && sawContent;
    }

    private static int TrimmedLength(string line)
    {
        var len = line.Length;
        if (len > 0 && line[len - 1] == '\r')
        {
            len--;
        }

        return len;
    }
}
