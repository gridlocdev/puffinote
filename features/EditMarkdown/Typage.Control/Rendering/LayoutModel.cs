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
    bool IsCollapsedBlankLine(int line);
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

    /// <summary>The total visual-row count across the document at the current width.</summary>
    public int TotalRows => RowPrefix.Length > 0 ? RowPrefix[^1] : 0;

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

    /// <summary>The Y coordinate (content space) at which <paramref name="block"/>'s first row begins.</summary>
    public double RowTop(int block) => RowPrefix[Math.Clamp(block, 0, RowPrefix.Length - 1)] * _host.Theme.LineHeight;

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
            var joins = !_host.RawTextMode && l > 0 && IsFlowLine(l) && IsFlowLine(l - 1) && !EndsWithHardBreak(l - 1);
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
                var collapsed = newStartLine[b + 1] - line == 1 && _host.IsCollapsedBlankLine(line);
                rowCounts[b] = collapsed ? 0 : BlockVisual(b).RowCount;
            }

            rowPrefix[b + 1] = rowPrefix[b] + rowCounts[b];
        }

        RowCounts = rowCounts;
        RowPrefix = rowPrefix;
        _layoutWidth = width;
        _layoutDirty = false;
        _firstDirtyLine = int.MaxValue;
    }

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

        if (_host.Model.EnteringState(line).Kind != BlockKind.Normal)
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
            or TokenType.FrontMatterFence or TokenType.FrontMatterText or TokenType.TableDelimiter)
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
            _host.OrderedMarkerText));
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
