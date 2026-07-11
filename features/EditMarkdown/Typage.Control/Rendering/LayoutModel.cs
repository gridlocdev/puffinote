using Typage.Engine.Document;
using Typage.Engine.Markdown;

namespace Typage.Control.Rendering;

/// <summary>
/// The live editor state the <see cref="LayoutModel"/> reads to group and measure blocks. Implemented by the
/// control so the model always sees the current document/model/theme/view state without holding stale
/// references, and can build a block's <see cref="VisualLine"/> (via the members that feed
/// <see cref="BlockLayoutContext"/>). Keeping this a narrow interface — rather than a pile of delegates — is
/// what makes the layout algorithm exercisable against a fake host in tests.
/// </summary>
internal interface ILayoutHost
{
    TextDocument Document { get; }
    MarkdownModel Model { get; }
    MarkdownTheme Theme { get; }
    double WrapWidth { get; }
    bool RawTextMode { get; }
    bool MarkersRevealed { get; }
    double CheckboxGutter { get; }
    int ListLevel(int line);
    string? OrderedMarkerText(int line, string lineText, int sLine, int enLine);
}

/// <summary>
/// Owns the editor's wrapped-layout state: the paragraph-block grouping (<see cref="BlockStartLine"/> /
/// <see cref="BlockOfLine"/>), the per-block visual-row counts and their prefix sums (<see cref="RowCounts"/>
/// / <see cref="RowPrefix"/>), and the cache of laid-out <see cref="VisualLine"/>s. <see cref="EnsureLayout"/>
/// rebuilds this incrementally — reusing every block before the first edited line — when the text, theme, or
/// wrap width changes, then answers the geometry queries the rest of the control needs (block-of-line,
/// block-at-row, row tops, caret resolution). It reads live state through an <see cref="ILayoutHost"/> and
/// builds visuals with a <see cref="BlockLayoutBuilder"/>, so the grouping/measurement logic can be tested
/// against a fake host with no live control.
/// </summary>
internal sealed class LayoutModel
{
    private readonly ILayoutHost _host;
    private readonly BlockLayoutBuilder _builder;
    private readonly Dictionary<int, VisualLine> _blockVisualCache = [];
    private readonly List<int> _blockVisualEvict = []; // scratch reused by EvictBlockVisualsFrom
    private bool _layoutDirty = true;
    private double _layoutWidth = double.NaN;
    private int _firstDirtyLine;

    public LayoutModel(ILayoutHost host, TextMeasurer measurer)
    {
        _host = host;
        _builder = new BlockLayoutBuilder(measurer);
    }

    public int[] BlockStartLine { get; private set; } = [0, 0];
    public int[] BlockOfLine { get; private set; } = [];
    public int[] RowCounts { get; private set; } = [];
    public int[] RowPrefix { get; private set; } = new int[1];

    /// <summary>Each block's kind, which decides its metrics (row height, margins, padding).</summary>
    public MarkdownBlockKind[] BlockKinds { get; private set; } = [];

    /// <summary>Each block's resolved metrics.</summary>
    public BlockMetrics[] BlockMetricsOf { get; private set; } = [];

    /// <summary>
    /// The Y coordinate of each block's <i>box</i> top (padding included), plus a final entry holding the
    /// document's total content height. Blocks have variable heights — a heading's rows are taller than a
    /// paragraph's, and the margins between them collapse — so unlike <see cref="RowPrefix"/> this is a
    /// prefix sum in <b>pixels</b>, and it is what drawing, hit-testing, and the scroll extent all read.
    /// </summary>
    public double[] BlockTops { get; private set; } = new double[1];

    /// <summary>Each block's total height: <c>PadTop + rows × RowHeight + PadBottom</c> (0 for a collapsed blank).</summary>
    public double[] BlockHeights { get; private set; } = [];

    /// <summary>The total visual-row count across the document at the current width.</summary>
    public int TotalRows => RowPrefix.Length > 0 ? RowPrefix[^1] : 0;

    /// <summary>The document's total height in pixels at the current width.</summary>
    public double ContentHeight => BlockTops.Length > 0 ? BlockTops[^1] : 0;

    /// <summary>The height of one wrapped row inside <paramref name="block"/>.</summary>
    public double RowHeight(int block) => Metrics(block).RowHeight;

    /// <summary>The top of <paramref name="block"/>'s box — its background/panel starts here, above any padding.</summary>
    public double BlockTop(int block) => BlockTops[Math.Clamp(block, 0, BlockTops.Length - 1)];

    /// <summary>The height of <paramref name="block"/>'s box, padding included.</summary>
    public double BlockHeight(int block) =>
        BlockHeights.Length == 0 ? 0 : BlockHeights[Math.Clamp(block, 0, BlockHeights.Length - 1)];

    private BlockMetrics Metrics(int block) => BlockMetricsOf.Length == 0
        ? _host.Theme.MetricsFor(MarkdownBlockKind.Paragraph)
        : BlockMetricsOf[Math.Clamp(block, 0, BlockMetricsOf.Length - 1)];

    /// <summary>The block containing content-space Y coordinate <paramref name="y"/> (binary search over
    /// <see cref="BlockTops"/>) — the pixel-space counterpart of <see cref="BlockAtRow"/>.</summary>
    public int BlockAtY(double y)
    {
        var n = BlockHeights.Length;
        if (n == 0)
        {
            return 0;
        }

        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (BlockTops[mid] <= y)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    /// <summary>Mark the layout stale from <paramref name="fromLine"/> down (an incremental edit); blocks above
    /// it are reused on the next <see cref="EnsureLayout"/>.</summary>
    public void MarkDirty(int fromLine)
    {
        _layoutDirty = true;
        _firstDirtyLine = Math.Min(_firstDirtyLine, fromLine);
    }

    /// <summary>Mark the whole layout stale and drop every cached visual — for a theme, view-mode, or
    /// wrap-width change, where every block must re-group and re-measure.</summary>
    public void InvalidateAll()
    {
        _layoutDirty = true;
        _firstDirtyLine = 0;
        _blockVisualCache.Clear();
    }

    public int BlockStartOffset(int block) => _host.Document.GetLineStart(BlockStartLine[block]);

    public int BlockEndOffset(int block) => _host.Document.GetLineEnd(BlockStartLine[block + 1] - 1);

    /// <summary>The Y coordinate (content space) at which <paramref name="block"/>'s first <i>text row</i> begins —
    /// its box top plus its top padding.</summary>
    public double RowTop(int block) => BlockTop(block) + Metrics(block).PadTop;

    /// <summary>
    /// Rebuild the paragraph-block grouping and per-block row-count cache (with prefix sums) when the text,
    /// theme, or wrap width has changed. Consecutive plain-paragraph lines are merged into one flowing
    /// block; every other line is a singleton. Measurement is cached, so an unchanged block is cheap.
    /// </summary>
    public void EnsureLayout()
    {
        var n = _host.Document.LineCount;
        var width = _host.WrapWidth;
        if (!_layoutDirty && _layoutWidth == width && BlockOfLine.Length == n)
        {
            return;
        }

        // A width change re-wraps every block; otherwise reuse everything before the first edited line, whose
        // layout (and all the context feeding it) is unchanged — mirroring the incremental tokenizer. A clean
        // sentinel here means we were dirtied without a tracked line, so rebuild fully to be safe.
        var firstDirty = _layoutWidth == width && _firstDirtyLine != int.MaxValue ? _firstDirtyLine : 0;

        // Group lines into blocks: a line joins the previous block only if it and the line above are both
        // plain paragraph text and the line above doesn't end in a hard break. (Cheap — no measurement.)
        var starts = new List<int>();
        var ofLine = new int[n];
        for (var l = 0; l < n; l++)
        {
            // Raw-text view shows the literal source: every line stands on its own row (no paragraph flow).
            // A plain continuation line flows into the block above whenever that block takes a lazy
            // continuation — a plain paragraph OR a list/task item (CommonMark lazy continuation), so an
            // author's hard-wrapped bullet/number/checkbox item reads as one flowing item, not two rows.
            var joins = !_host.RawTextMode && l > 0 && IsFlowLine(l) && AcceptsContinuation(l - 1) && !EndsWithHardBreak(l - 1);
            if (!joins)
            {
                starts.Add(l);
            }

            ofLine[l] = starts.Count - 1;
        }

        var blockCount = starts.Count;
        var oldStarts = BlockStartLine;
        var oldRowCounts = RowCounts;

        var newStartLine = new int[blockCount + 1];
        for (var b = 0; b < blockCount; b++)
        {
            newStartLine[b] = starts[b];
        }

        newStartLine[blockCount] = n;

        // Leading blocks identical to the previous layout (same boundaries, ending one line clear of the first
        // edit so the flow-join the edit might change is excluded): their measured rows and cached visuals
        // carry over untouched.
        var reuse = 0;
        while (reuse < blockCount && reuse < oldRowCounts.Length
               && newStartLine[reuse] == oldStarts[reuse]
               && newStartLine[reuse + 1] == oldStarts[reuse + 1]
               && newStartLine[reuse + 1] <= firstDirty - 1)
        {
            reuse++;
        }

        // Cached visuals from the first rebuilt block on are stale (their indices may now name different
        // blocks); the reused prefix stays. BlockVisual repopulates the rest as the row loop measures them.
        EvictBlockVisualsFrom(reuse);

        BlockStartLine = newStartLine;
        BlockOfLine = ofLine;

        var rowCounts = new int[blockCount];
        var rowPrefix = new int[blockCount + 1];
        for (var b = 0; b < blockCount; b++)
        {
            if (b < reuse)
            {
                rowCounts[b] = oldRowCounts[b]; // unchanged block — keep its measured row count
            }
            else
            {
                // A soft paragraph separator (a single collapsed blank line) takes no rows, so two paragraphs
                // render one line apart instead of with an empty line between them.
                var line = newStartLine[b];
                var collapsed = newStartLine[b + 1] - line == 1 && IsCollapsedBlankLine(line);
                rowCounts[b] = collapsed ? 0 : BlockVisual(b).RowCount;
            }

            rowPrefix[b + 1] = rowPrefix[b] + rowCounts[b];
        }

        RowCounts = rowCounts;
        RowPrefix = rowPrefix;
        BuildVerticalRhythm(blockCount, rowCounts);
        _layoutWidth = width;
        _layoutDirty = false;
        _firstDirtyLine = int.MaxValue;
    }

    /// <summary>
    /// Resolve every block's kind and metrics, then accumulate the pixel prefix sums that place them.
    ///
    /// <para>Two rules make the result read as a document rather than a stack of lines. Margins <b>collapse</b>
    /// (<see cref="MarkdownTheme.GapBetween"/>): the gap between two blocks is the larger of the upper one's
    /// SpaceAfter and the lower one's SpaceBefore, not their sum. And a <b>collapsed blank</b> separator — the
    /// single blank source line between two paragraphs — is transparent: it takes no height <i>and</i> no gap of
    /// its own, so the two paragraphs it separates collapse against each other directly. Were it opaque, a
    /// paragraph's 12px SpaceAfter would be paid twice (once into the blank, once out of it) and every paragraph
    /// break would be double-spaced.</para>
    /// </summary>
    private void BuildVerticalRhythm(int blockCount, int[] rowCounts)
    {
        var theme = _host.Theme;
        var kinds = new MarkdownBlockKind[blockCount];
        for (var b = 0; b < blockCount; b++)
        {
            kinds[b] = KindOf(b);
        }

        var metrics = new BlockMetrics[blockCount];
        var heights = new double[blockCount];
        for (var b = 0; b < blockCount; b++)
        {
            // A run is a maximal stretch of same-kind blocks — the source lines of one fenced-code panel,
            // blockquote, or table. Only its outer edges carry the inner padding, so the run reads as one object.
            var firstOfRun = b == 0 || kinds[b - 1] != kinds[b];
            var lastOfRun = b == blockCount - 1 || kinds[b + 1] != kinds[b];
            var m = theme.MetricsFor(kinds[b], HeadingLevelOf(b), firstOfRun, lastOfRun);
            metrics[b] = m;
            heights[b] = rowCounts[b] == 0 ? 0 : m.PadTop + (rowCounts[b] * m.RowHeight) + m.PadBottom;
        }

        var tops = new double[blockCount + 1];
        var prev = -1; // the last block that actually occupies space; -1 until we've seen one
        for (var b = 0; b < blockCount; b++)
        {
            if (b > 0)
            {
                // A zero-height block (a collapsed blank) contributes neither height nor gap: skip it entirely
                // and collapse across it. The document's first block takes no SpaceBefore — a 48px void above an
                // opening H1 would just look like broken padding.
                //
                // The run-closing rule only applies to *touching* blocks. Two blockquotes with a blank line
                // between them are two quotes, not one: the blank collapses to zero height, which would otherwise
                // make them look adjacent here and weld them into a single bar.
                //
                // A hard break (a trailing "\" or two spaces) closes the gap entirely. It splits the paragraph
                // into two blocks — the next line must start a new row — but it is a *line* break, not a
                // paragraph break, so paying the paragraph's SpaceAfter across it would render it identically to
                // a blank-line separator and erase the only difference between the two.
                var gap = prev < 0 || (prev == b - 1 && EndsWithHardBreak(BlockStartLine[b] - 1)) ? 0
                    : prev == b - 1 ? MarkdownTheme.GapBetween(metrics[prev], kinds[prev], metrics[b], kinds[b])
                    : Math.Max(metrics[prev].SpaceAfter, metrics[b].SpaceBefore);
                tops[b] = tops[b - 1] + heights[b - 1] + (heights[b] == 0 ? 0 : gap);
            }

            if (heights[b] > 0)
            {
                prev = b;
            }
        }

        tops[blockCount] = blockCount == 0 ? 0 : tops[blockCount - 1] + heights[blockCount - 1];

        BlockKinds = kinds;
        BlockMetricsOf = metrics;
        BlockHeights = heights;
        BlockTops = tops;
    }

    /// <summary>The heading level of <paramref name="block"/> (1–6), or 0 when it isn't a heading.</summary>
    private int HeadingLevelOf(int block)
    {
        var toks = _host.Model.LineTokens(BlockStartLine[block]);
        return toks.Count > 0 && toks[0].Type == TokenType.HeadingMarker && int.TryParse(toks[0].Meta, out var l) ? l : 0;
    }

    /// <summary>The blockquote nesting depth of <paramref name="block"/> (1 for <c>&gt;</c>, 2 for <c>&gt;&gt;</c>, …),
    /// or 0 when it isn't a quote.</summary>
    public int QuoteDepthOf(int block)
    {
        if (_host.RawTextMode || block < 0 || block >= BlockStartLine.Length - 1)
        {
            return 0;
        }

        var toks = _host.Model.LineTokens(BlockStartLine[block]);
        return toks.Count > 0 && toks[0].Type == TokenType.BlockquoteMarker && int.TryParse(toks[0].Meta, out var d) ? d : 0;
    }

    /// <summary>
    /// Classify <paramref name="block"/> from the leading token of its first line — the same signal
    /// <see cref="IsFlowLine"/> groups on, so the classification and the grouping can never disagree.
    /// In raw-source view everything is plain source, so every block is a paragraph and the rhythm goes flat.
    /// </summary>
    private MarkdownBlockKind KindOf(int block)
    {
        if (_host.RawTextMode)
        {
            return MarkdownBlockKind.Paragraph;
        }

        var line = BlockStartLine[block];
        if (_host.Model.EnteringState(line).Kind == BlockKind.FrontMatter)
        {
            // Inside the front matter — the state, not the token, is what says so (its body lines carry none).
            return MarkdownBlockKind.FrontMatter;
        }

        if (IsCodeLine(line))
        {
            return MarkdownBlockKind.Code;
        }

        var toks = _host.Model.LineTokens(line);
        if (toks.Count == 0)
        {
            return MarkdownBlockKind.Paragraph;
        }

        switch (toks[0].Type)
        {
            case TokenType.HeadingMarker:
                return MarkdownBlockKind.Heading;
            case TokenType.BlockquoteMarker:
                return MarkdownBlockKind.Blockquote;
            case TokenType.ListMarker or TokenType.TaskMarker:
                return MarkdownBlockKind.ListItem;
            case TokenType.HorizontalRule:
                return MarkdownBlockKind.Rule;
            case TokenType.SetextUnderline:
                // Collapsed to zero rows, so its metrics never place anything — but it must not read as a
                // Paragraph either, or it would contribute a paragraph's margin around a heading it belongs to.
                return MarkdownBlockKind.Heading;
            case TokenType.CodeFence or TokenType.CodeText:
                return MarkdownBlockKind.Code;
            case TokenType.FrontMatterFence or TokenType.FrontMatterText:
                return MarkdownBlockKind.FrontMatter;
            case TokenType.TableDelimiter:
                return MarkdownBlockKind.Table;
        }

        // A pipe-table header/body row carries single-'|' Marker tokens (only the table tokenizer emits those).
        foreach (var t in toks)
        {
            if (t.Type == TokenType.Marker && t.Length == 1 && _host.Document[t.Start] == '|')
            {
                return MarkdownBlockKind.Table;
            }
        }

        return MarkdownBlockKind.Paragraph;
    }

    /// <summary>
    /// Whether <paramref name="line"/> is a row of a code block — fenced or indented — and so is panelled as code.
    ///
    /// <para>The entering block state alone cannot answer this, and reading it as if it could is what put a heading
    /// inside the code panel above it: an indented code block ends <b>lazily</b>, at the first dedented line, so that
    /// line is still <i>entered</i> with the IndentedCode state even though the tokenizer then classifies it as an
    /// ordinary heading/paragraph. The tokens settle it — a code row carries the fence marker or code text — with a
    /// forward scan for the one case tokens can't answer: a blank line, which keeps an indented block open only when
    /// indented code actually resumes below it. A blank that merely trails the block (the empty line your caret sits
    /// on while typing below one) is not part of it.</para>
    /// </summary>
    public bool IsCodeLine(int line)
    {
        var tokens = _host.Model.LineTokens(line);
        if (tokens.Count > 0)
        {
            return tokens[0].Type is TokenType.CodeFence or TokenType.CodeText;
        }

        var entering = _host.Model.EnteringState(line).Kind;
        if (entering == BlockKind.FencedCode)
        {
            return true; // a blank row inside a fenced block is code until the fence closes
        }

        if (entering == BlockKind.IndentedCode)
        {
            for (var l = line + 1; l < _host.Document.LineCount; l++)
            {
                var toks = _host.Model.LineTokens(l);
                if (toks.Count == 0)
                {
                    continue; // another interior blank — keep scanning forward
                }

                return toks[0].Type == TokenType.CodeText; // indented code resumes → this blank is interior
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="line"/> sits inside a code block or the front matter — the two states whose
    /// rows never join a paragraph. Not simply "its entering state isn't Normal": see <see cref="IsCodeLine"/> for
    /// why the line that <i>ends</i> an indented code block still enters with the code state.</summary>
    private bool InCodeOrFrontMatter(int line) =>
        IsCodeLine(line) || _host.Model.EnteringState(line).Kind == BlockKind.FrontMatter;

    /// <summary>
    /// Whether <paramref name="line"/> is a <b>soft paragraph separator</b>: an empty line directly below a
    /// non-blank one, in ordinary (non-code, non-front-matter) flow. Pressing Enter writes such a blank (a
    /// <c>\n\n</c> paragraph break); it is collapsed to zero visual rows so two paragraphs sit one line apart
    /// (regular spacing) instead of leaving an empty line, and the caret glides over it as one step. A second
    /// consecutive blank is <i>not</i> collapsed — that's a deliberately empty line — and a blank inside a code
    /// block stays part of the code (<see cref="IsCodeLine"/>, which is what tells an <i>interior</i> blank from
    /// the one that merely trails the block). The line must be <b>truly empty</b>: a whitespace-only line (an
    /// auto-indented new line) is a real row and keeps its caret, so collapsing it would strand the caret at the
    /// end of the line above (the "Enter doesn't move to the next line" bug).
    /// </summary>
    public bool IsCollapsedBlankLine(int line)
    {
        // Raw-text view shows every blank line as a real, editable row (no soft collapsing).
        var doc = _host.Document;
        if (_host.RawTextMode || line < 0 || line >= doc.LineCount)
        {
            return false;
        }

        // A setext underline ("===" / "---") is pure markup. Concealing it would leave a blank row where the
        // underline was, which in a WYSIWYG view is just as wrong as showing the "===" itself — so its row
        // collapses away entirely, exactly like a soft paragraph separator. The heading it marks is the line
        // above, which draws as a heading; raw-source view shows the underline again.
        var toks = _host.Model.LineTokens(line);
        if (toks.Count > 0)
        {
            return toks[0].Type == TokenType.SetextUnderline;
        }

        return line > 0
            && IsEmpty(line) && !IsBlank(line - 1)
            && !InCodeOrFrontMatter(line);
    }

    /// <summary>Whether <paramref name="line"/> has no characters at all (a trailing CR aside) — stricter than
    /// <see cref="IsBlank"/>: a whitespace-only line is a real editable row, not an empty separator.</summary>
    private bool IsEmpty(int line)
    {
        var text = _host.Document.GetLineText(line);
        return text.Length == 0 || (text.Length == 1 && text[0] == '\r');
    }

    /// <summary>Whether <paramref name="line"/> is blank (empty or whitespace-only).</summary>
    private bool IsBlank(int line) => _host.Document.GetLineText(line).Trim().Length == 0;

    /// <summary>Drop cached block visuals at indices &gt;= <paramref name="from"/> (those blocks are being rebuilt).</summary>
    private void EvictBlockVisualsFrom(int from)
    {
        if (from == 0)
        {
            _blockVisualCache.Clear();
            return;
        }

        if (_blockVisualCache.Count == 0)
        {
            return;
        }

        _blockVisualEvict.Clear();
        foreach (var key in _blockVisualCache.Keys)
        {
            if (key >= from)
            {
                _blockVisualEvict.Add(key);
            }
        }

        foreach (var key in _blockVisualEvict)
        {
            _blockVisualCache.Remove(key);
        }
    }

    /// <summary>
    /// Whether <paramref name="line"/> is a plain-paragraph line that flows with its neighbours — non-blank,
    /// ordinary block state (not code/front matter), no leading block/list/quote/heading marker, and not a
    /// table row. Headings, lists, quotes, rules, code, and blank lines all break the flow.
    /// </summary>
    private bool IsFlowLine(int line)
    {
        var doc = _host.Document;
        var text = doc.GetLineText(line);
        if (text.Trim().Length == 0)
        {
            return false; // a blank line separates paragraphs
        }

        // A line that starts with indentation is laid out on its own row: the user's leading tabs/spaces
        // are deliberate, so it neither flows into the line above nor absorbs the line below (an indented
        // line under plain prose otherwise merged into it with the newline becoming a space).
        if (text[0] == ' ' || text[0] == '\t')
        {
            return false;
        }

        if (InCodeOrFrontMatter(line))
        {
            return false; // inside fenced/indented code or front matter
        }

        var toks = _host.Model.LineTokens(line);
        if (toks.Count == 0)
        {
            return false;
        }

        if (toks[0].Type is TokenType.HeadingMarker or TokenType.BlockquoteMarker or TokenType.ListMarker
            or TokenType.TaskMarker or TokenType.HorizontalRule or TokenType.CodeFence or TokenType.CodeText
            or TokenType.FrontMatterFence or TokenType.FrontMatterText or TokenType.TableDelimiter
            or TokenType.SetextUnderline)
        {
            return false;
        }

        // A pipe-table body/header row carries single-'|' Marker tokens (only the table tokenizer emits
        // those); keep it out of paragraph flow.
        foreach (var t in toks)
        {
            if (t.Type == TokenType.Marker && t.Length == 1 && doc[t.Start] == '|')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a plain continuation line below <paramref name="line"/> may lazily flow into its block. True for
    /// a plain paragraph line (<see cref="IsFlowLine"/>) and for a list item — bulleted, numbered, or a task
    /// checkbox — whose text a following unmarked line continues (CommonMark lazy continuation). Headings,
    /// horizontal rules, blockquotes, code, front matter, table rows, blank lines, and indented lines take no
    /// continuation, so a line under them stays its own block.
    /// </summary>
    private bool AcceptsContinuation(int line)
    {
        if (IsFlowLine(line))
        {
            return true; // a plain paragraph line
        }

        // Otherwise only a list/task item takes a continuation. Mirror IsFlowLine's guards: a blank, indented,
        // or non-Normal (code/front-matter) line never does.
        var doc = _host.Document;
        var text = doc.GetLineText(line);
        if (text.Length == 0 || text[0] == ' ' || text[0] == '\t')
        {
            return false;
        }

        if (InCodeOrFrontMatter(line))
        {
            return false;
        }

        var toks = _host.Model.LineTokens(line);
        return toks.Count > 0 && toks[0].Type is TokenType.ListMarker or TokenType.TaskMarker;
    }

    /// <summary>Whether <paramref name="line"/> ends in a hard line break (so the next line shouldn't flow into it).</summary>
    private bool EndsWithHardBreak(int line)
    {
        var toks = _host.Model.LineTokens(line);
        return toks.Count > 0 && toks[^1].Type == TokenType.HardBreak;
    }

    /// <summary>The block <paramref name="line"/> belongs to.</summary>
    public int BlockOf(int line)
    {
        EnsureLayout();
        return BlockOfLine[Math.Clamp(line, 0, BlockOfLine.Length - 1)];
    }

    /// <summary>The block containing global visual <paramref name="row"/> (binary search over the prefix sums).</summary>
    public int BlockAtRow(int row)
    {
        var n = RowCounts.Length;
        if (n == 0)
        {
            return 0;
        }

        row = Math.Clamp(row, 0, Math.Max(0, TotalRows - 1));
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (RowPrefix[mid] <= row)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    /// <summary>The laid-out <see cref="VisualLine"/> for <paramref name="block"/>, cached while the layout is
    /// stable (the cache is cleared by <see cref="EnsureLayout"/> on any structural or width change). On a miss
    /// the <see cref="BlockLayoutBuilder"/> builds it from a snapshot of the current document/model/theme/view state.</summary>
    public VisualLine BlockVisual(int block)
    {
        if (_blockVisualCache.TryGetValue(block, out var cached))
        {
            return cached;
        }

        var visual = _builder.Build(block, new BlockLayoutContext(
            _host.Document, _host.Model, _host.Theme, BlockStartLine,
            _host.RawTextMode, _host.MarkersRevealed, _host.WrapWidth, _host.CheckboxGutter,
            _host.ListLevel, _host.OrderedMarkerText));
        _blockVisualCache[block] = visual;
        return visual;
    }

    /// <summary>The caret's block, its start offset, laid-out block, and block-relative column for <paramref name="offset"/>.</summary>
    public (int Block, int BlockStart, VisualLine Visual, int Col) ResolveCaret(int offset)
    {
        EnsureLayout();
        var doc = _host.Document;
        var block = BlockOf(doc.GetPosition(offset).Line);

        // A collapsed blank block has no rows to position a caret on; snap to the end of the previous
        // (content) block so an offset that lands on a separator still resolves to a drawn position.
        while (block > 0 && RowCounts.Length > block && RowCounts[block] == 0)
        {
            block--;
            offset = BlockEndOffset(block);
        }

        var blockStart = BlockStartOffset(block);
        var visual = BlockVisual(block);
        var col = Math.Clamp(offset - blockStart, 0, visual.RawLength);
        return (block, blockStart, visual, col);
    }
}
