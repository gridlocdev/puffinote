using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Typage.Engine.Editing;
using Typage.Engine.Markdown;


namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    protected override Size MeasureOverride(Size availableSize)
    {
        // Under logical scrolling the presenter constrains us to the viewport; report that. When a
        // dimension is unconstrained (not in a scroll viewer), fall back to the content size.
        var w = double.IsFinite(availableSize.Width) ? availableSize.Width : _extentWidth;
        EnsureLayout();
        var h = double.IsFinite(availableSize.Height)
            ? availableSize.Height
            : Math.Max(_theme.LineHeight, TotalRows * _theme.LineHeight);
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_viewport != finalSize)
        {
            var widthChanged = Math.Abs(_viewport.Width - finalSize.Width) > 0.5;
            _viewport = finalSize;
            if (widthChanged)
            {
                InvalidateVisual(); // a width change re-wraps the lines
            }

            RaiseScrollInvalidated(EventArgs.Empty);
        }

        return finalSize;
    }

    private Vector ClampOffset(Vector value)
    {
        var extent = ((IScrollable)this).Extent;
        var maxX = Math.Max(0, extent.Width - _viewport.Width);
        var maxY = Math.Max(0, extent.Height - _viewport.Height);
        return new Vector(Math.Clamp(value.X, 0, maxX), Math.Clamp(value.Y, 0, maxY));
    }

    // ----- wrapped layout (variable line heights) -----

    /// <summary>The content width lines wrap to (0 disables wrapping, e.g. before the control is arranged).</summary>
    private double WrapWidth()
    {
        if (!_wordWrap)
        {
            return 0;
        }

        var w = _viewport.Width > 0 ? _viewport.Width : Bounds.Width;
        return w > 0 ? Math.Max(40, w - PaddingLeft * 2) : 0;
    }

    /// <summary>
    /// Rebuild the paragraph-block grouping and per-block row-count cache (with prefix sums) when the text,
    /// theme, or wrap width has changed. Consecutive plain-paragraph lines are merged into one flowing
    /// block; every other line is a singleton. Measurement is cached, so an unchanged block is cheap.
    /// </summary>
    private void EnsureLayout()
    {
        var n = _buffer.Document.LineCount;
        var width = WrapWidth();
        if (!_layoutDirty && _layoutWidth == width && _blockOfLine.Length == n)
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
            var joins = !RawTextMode && l > 0 && IsFlowLine(l) && IsFlowLine(l - 1) && !EndsWithHardBreak(l - 1);
            if (!joins)
            {
                starts.Add(l);
            }

            ofLine[l] = starts.Count - 1;
        }

        var blockCount = starts.Count;
        var oldStarts = _blockStartLine;
        var oldRowCounts = _rowCounts;

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

        _blockStartLine = newStartLine;
        _blockOfLine = ofLine;

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

        _rowCounts = rowCounts;
        _rowPrefix = rowPrefix;
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
        var doc = _buffer.Document;
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

        if (_model.EnteringState(line).Kind != BlockKind.Normal)
        {
            return false; // inside fenced/indented code or front matter
        }

        var toks = _model.LineTokens(line);
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
        var toks = _model.LineTokens(line);
        return toks.Count > 0 && toks[^1].Type == TokenType.HardBreak;
    }

    /// <summary>The block <paramref name="line"/> belongs to.</summary>
    private int BlockOf(int line)
    {
        EnsureLayout();
        return _blockOfLine[Math.Clamp(line, 0, _blockOfLine.Length - 1)];
    }

    /// <summary>The block containing global visual <paramref name="row"/> (binary search over the prefix sums).</summary>
    private int BlockAtRow(int row)
    {
        var n = _rowCounts.Length;
        if (n == 0)
        {
            return 0;
        }

        row = Math.Clamp(row, 0, Math.Max(0, TotalRows - 1));
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (_rowPrefix[mid] <= row)
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

    /// <summary>The caret's block, its start offset, laid-out block, and block-relative column for <paramref name="offset"/>.</summary>
    private (int Block, int BlockStart, VisualLine Visual, int Col) ResolveCaret(int offset)
    {
        EnsureLayout();
        var doc = _buffer.Document;
        var block = BlockOf(doc.GetPosition(offset).Line);

        // A collapsed blank block has no rows to position a caret on; snap to the end of the previous
        // (content) block so an offset that lands on a separator still resolves to a drawn position.
        while (block > 0 && _rowCounts.Length > block && _rowCounts[block] == 0)
        {
            block--;
            offset = BlockEndOffset(block);
        }

        var blockStart = BlockStartOffset(block);
        var visual = BlockVisual(block);
        var col = Math.Clamp(offset - blockStart, 0, visual.RawLength);
        return (block, blockStart, visual, col);
    }

    // ----- rendering -----

    /// <summary>
    /// The leading front-matter block (PRD §7.4): line 0's opening <c>---</c> through its closing
    /// fence, or null when the document has none. Front matter exists only when a closing fence is
    /// present (a lone <c>---</c> tokenizes as a horizontal rule), so a found opening implies a close.
    /// </summary>
    private (int Start, int End)? FrontMatterBlock()
    {
        var doc = _buffer.Document;
        if (doc.LineCount == 0)
        {
            return null;
        }

        var first = _model.LineTokens(0);
        if (first.Count == 0 || first[0].Type != TokenType.FrontMatterFence)
        {
            return null;
        }

        for (var line = 1; line < doc.LineCount; line++)
        {
            var tokens = _model.LineTokens(line);
            if (tokens.Count > 0 && tokens[0].Type == TokenType.FrontMatterFence)
            {
                return (0, line);
            }
        }

        return null;
    }

    /// <summary>The total visual-row count across the document at the current width; for tests.</summary>
    internal int TotalRowsForTest()
    {
        EnsureLayout();
        return TotalRows;
    }

    /// <summary>The global visual row at which <paramref name="line"/>'s block starts; for tests (layout equivalence).</summary>
    internal int BlockTopRowForTest(int line)
    {
        EnsureLayout();
        return _rowPrefix[BlockOf(line)];
    }

    /// <summary>The global visual row the caret is actually drawn on, mirroring RenderBlock's placement; for tests.</summary>
    internal int CaretDrawnRowForTest()
    {
        EnsureLayout();
        var sel = _buffer.Selection;
        var caretBlock = ResolveCaret(sel.Caret).Block;
        var blockStart = BlockStartOffset(caretBlock);
        var visual = BlockVisual(caretBlock);
        var col = Math.Clamp(sel.Caret - blockStart, 0, visual.RawLength);
        return _rowPrefix[caretBlock] + visual.RowOf(col);
    }

}
