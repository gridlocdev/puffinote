using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    public override void Render(DrawingContext context)
    {
        var doc = Buffer.Document;
        context.FillRectangle(EditorTheme.Background, new Rect(Bounds.Size));

        EnsureLayout();
        var caret = Buffer.Selection;
        // The block the caret is drawn in is the *resolved* one: if the offset lands on a collapsed blank
        // separator (e.g. Ctrl+End on a doc ending in "\n"), it snaps to the previous content block — whose
        // row is actually drawn — so the caret never disappears into a skipped zero-row block.
        var caretBlock = ResolveCaret(caret.Caret).Block;

        // Draw only the blocks that intersect the viewport (virtualization over variable block heights).
        // The viewport height is 0 until the control is arranged in a scroll viewer.
        var viewportH = _viewport.Height > 0 ? _viewport.Height : Bounds.Height;
        var contentTop = _offset.Y - PaddingTop;
        var first = BlockAtY(contentTop);
        var last = Math.Min(_rowCounts.Length, BlockAtY(contentTop + viewportH) + 1);

        // Content is drawn in document space, inset by the padding and shifted by the scroll offset.
        using (context.PushTransform(Matrix.CreateTranslation(PaddingLeft - _offset.X, PaddingTop - _offset.Y)))
        {
            if (!RawTextMode)
            {
                DrawPropertiesPanel(context, first, last); // front-matter "Properties" backdrop, under the text
                DrawBlockPanels(context, first, last);     // code-block and blockquote panels, under the text
            }

            var maxWidth = _extentWidth;
            for (var block = first; block < last; block++)
            {
                if (_rowCounts[block] == 0)
                {
                    continue; // a collapsed blank separator takes no rows and draws nothing
                }

                maxWidth = Math.Max(maxWidth, RenderBlock(context, doc, block, caret, caretBlock));
            }

            DrawReorderIndicator(context);
            if (!RawTextMode)
            {
                DrawCodeCopyButtons(context, first, last); // no code-block chrome in the plain-source view
            }

            // Only relevant when wrapping is off: grow the horizontal extent as wide lines come into view.
            if (!WordWrap && maxWidth > _extentWidth)
            {
                _extentWidth = maxWidth;
                // Notify the scroll viewer AFTER this render pass — invalidating a visual (the scroll
                // bar) during render throws ("Visual was invalidated during the render pass").
                Dispatcher.UIThread.Post(() => RaiseScrollInvalidated(EventArgs.Empty));
            }
        }
    }

    /// <summary>
    /// Draw the "Properties" panel behind the front-matter block: a tinted backdrop spanning its
    /// rows plus a right-aligned label on the opening-fence row. Drawn under the text and without
    /// changing any glyph layout, so caret/selection/hit-testing stay exact (offsets remain 1:1).
    /// True folding of the block belongs with the §7.4 concealment work (the offset↔visual map).
    /// </summary>
    private void DrawPropertiesPanel(DrawingContext context, int firstVisibleBlock, int lastVisibleBlock)
    {
        if (FrontMatterBlock() is not { } fm)
        {
            return;
        }

        var startBlock = BlockOf(fm.Start);
        var endBlock = BlockOf(fm.End);
        if (endBlock < firstVisibleBlock || startBlock >= lastVisibleBlock)
        {
            return;
        }

        var panelRight = ContentRight();
        var startTop = BlockTop(startBlock);
        var endBottom = BlockTop(endBlock) + BlockHeight(endBlock);
        context.FillRectangle(EditorTheme.PropertiesBackground, new Rect(-8, startTop, panelRight + 8, endBottom - startTop), 6);

        // "Properties" label on the opening-fence row (its source is only "---", so no text overlap).
        var label = _measurer.GetFormattedText("Properties", EditorTheme.Resolve(TokenType.FrontMatterFence), _dimMarker);
        var labelY = RowTop(startBlock) + (RowHeight(startBlock) - label.Height) / 2;
        context.DrawText(label, new Point(panelRight - label.Width - 6, labelY));
    }

    /// <summary>
    /// Faint dotted lines just above the first and just below the last line of the block being reordered
    /// (Alt+Shift+Up/Down), so it's visually clear what's moving. Drawn in document space inside the
    /// content transform; a no-op unless a reorder session is active.
    /// </summary>
    private void DrawReorderIndicator(DrawingContext context)
    {
        if (!_reorderActive)
        {
            return;
        }

        var n = Buffer.Document.LineCount;
        if (_reorderStartLine < 0 || _reorderEndLine >= n || _reorderStartLine > _reorderEndLine)
        {
            _reorderActive = false;
            return;
        }

        var top = BlockTop(BlockOf(_reorderStartLine));
        var bottomBlock = BlockOf(_reorderEndLine);
        var bottom = BlockTop(bottomBlock) + BlockHeight(bottomBlock);
        var pen = new Pen(_dimMarker, 1) { DashStyle = new DashStyle([2, 2], 0) };
        var left = -6.0;
        var right = ContentRight();
        context.DrawLine(pen, new Point(left, top), new Point(right, top));
        context.DrawLine(pen, new Point(left, bottom), new Point(right, bottom));
    }

    /// <summary>
    /// Draw the background panels for code blocks and blockquotes: one rectangle per <i>run</i>, not per block.
    ///
    /// <para>A fenced-code block and a blockquote are each spelled as one source line per block, but they read as
    /// a single object — so the panel is drawn once across the whole run, with the run's rounded corners and its
    /// left accent bar spanning every row. Drawing it per block would round every line's corners and leave the
    /// bar broken across the run's inner padding and across any wrapped continuation row.</para>
    ///
    /// <para>A run is expanded past the visible range in both directions before drawing: scrolling into the
    /// middle of a long code block must still paint the panel, even though the block that opens it was culled.</para>
    /// </summary>
    private void DrawBlockPanels(DrawingContext context, int firstBlock, int lastBlock)
    {
        var count = _rowCounts.Length;
        var b = firstBlock;
        while (b < lastBlock)
        {
            var kind = KindOfBlock(b);
            if (kind is not (MarkdownBlockKind.Code or MarkdownBlockKind.Blockquote))
            {
                b++;
                continue;
            }

            var start = b;
            while (start > 0 && KindOfBlock(start - 1) == kind)
            {
                start--;
            }

            var end = b;
            while (end + 1 < count && KindOfBlock(end + 1) == kind)
            {
                end++;
            }

            var top = BlockTop(start);
            var rect = new Rect(-8, top, ContentRight() + 16, BlockTop(end) + BlockHeight(end) - top);
            if (kind == MarkdownBlockKind.Code)
            {
                var r = EditorTheme.CodeBlockCornerRadius;
                context.DrawRectangle(EditorTheme.CodeBlockBackground, new Pen(EditorTheme.CodeBlockBorder, 0.5), rect, r, r);
            }
            else
            {
                // Square corners: a rounded corner on a single-sided border reads as broken, not as a quote.
                context.FillRectangle(EditorTheme.QuoteBackground, rect);
                context.FillRectangle(EditorTheme.QuoteBar, rect.WithWidth(EditorTheme.QuoteBarWidth));
            }

            b = end + 1;
        }
    }

    /// <summary>
    /// A "copy" button at the top-right of each code block (fenced or indented) visible in the viewport, plus
    /// a transient "Copied!" label after a click. Drawn in document space; the hit rect is stored in control
    /// space (the inverse of the render transform) so <see cref="OnPointerPressed"/> can test pointer clicks.
    /// </summary>
    private void DrawCodeCopyButtons(DrawingContext context, int firstBlock, int lastBlock)
    {
        _codeCopyButtons.Clear();
        var doc = Buffer.Document;
        for (var b = firstBlock; b < lastBlock; b++)
        {
            var line = _blockStartLine[b];
            if (!IsCodeLine(line) || (line > 0 && IsCodeLine(line - 1)))
            {
                continue; // only the first line of each contiguous code run carries the button
            }

            var endLine = line;
            while (endLine + 1 < doc.LineCount && IsCodeLine(endLine + 1))
            {
                endLine++;
            }

            const double w = 24;
            const double h = 18;
            var x = ContentRight() - w - 8;
            var y = RowTop(b) + (RowHeight(b) - h) / 2;
            var rect = new Rect(x, y, w, h);
            context.FillRectangle(EditorTheme.CodeBlockBackground, rect, 4);
            DrawCopyGlyph(context, rect);

            if (_copiedFeedbackLine == line)
            {
                var label = _measurer.GetFormattedText("Copied!", EditorTheme.Resolve(TokenType.CodeText), EditorTheme.LinkForeground);
                context.DrawText(label, new Point(x - label.Width - 6, y + (h - label.Height) / 2));
            }

            // Store the hit rect in control space (undo the PaddingLeft/Top − scroll-offset render transform).
            _codeCopyButtons.Add((new Rect(x + PaddingLeft - _offset.X, y + PaddingTop - _offset.Y, w, h), line, endLine));
        }
    }

    /// <summary>Draw a two-overlapping-rectangles "copy" glyph centred in <paramref name="button"/>.</summary>
    private void DrawCopyGlyph(DrawingContext context, Rect button)
    {
        var pen = new Pen(EditorTheme.MarkerForeground, 1.2);
        var cx = button.X + button.Width / 2;
        var cy = button.Y + button.Height / 2;
        const double gw = 8, gh = 9, off = 2;
        var back = new Rect(cx - gw / 2 + off, cy - gh / 2 - off, gw, gh);
        var front = new Rect(cx - gw / 2 - off, cy - gh / 2 + off, gw, gh);
        context.DrawRectangle(EditorTheme.CodeBlockBackground, pen, back, 1.5, 1.5);
        context.DrawRectangle(EditorTheme.CodeBlockBackground, pen, front, 1.5, 1.5);
    }

    private double RenderBlock(DrawingContext context, TextDocument doc, int block, Selection sel, int caretBlock)
    {
        var lineH = RowHeight(block);
        var rowsTop = RowTop(block);
        var startLine = _blockStartLine[block];
        var blockStart = doc.GetLineStart(startLine);
        var visual = BlockVisual(block);

        // A horizontal rule has no real text to place a visible caret on, so when the caret is on it
        // (WYSIWYG view, no range selection) highlight the whole rule row in the selection colour to show
        // the rule line itself is the focused / selected line.
        if (IsFocused && ShouldHighlightRuleLine(startLine))
        {
            context.FillRectangle(EditorTheme.SelectionBackground, new Rect(-8, BlockTop(block), ContentRight() + 16, BlockHeight(block)));
        }

        // Search-match highlights (under the text), then the selection on top.
        if (_searchMatches.Count > 0)
        {
            DrawSearchMatches(context, blockStart, rowsTop, lineH, visual);
        }

        if (sel.Length > 0)
        {
            DrawSelection(context, blockStart, rowsTop, lineH, sel, visual);
        }

        // Draw every visual run on its wrapped row: concealed marker runs vanish (zero width, not drawn);
        // text and decorations advance the cursor exactly as the offset↔visual map measured them.
        foreach (var run in visual.Runs)
        {
            var rowTop = rowsTop + run.Row * lineH;
            switch (run.Kind)
            {
                case VisualRunKind.Text:
                    DrawTextRun(context, run, rowTop, lineH);
                    break;
                case VisualRunKind.Decoration:
                    DrawDecoration(context, run, rowTop, lineH);
                    break;
            }
        }

        // Caret — placed through the map (correct amid hidden markers) on its wrapped row. It takes the row's
        // own height, so it grows on a heading line rather than staying body-sized.
        if (caretBlock == block && IsFocused)
        {
            var col = Math.Clamp(sel.Caret - blockStart, 0, visual.RawLength);
            var caretX = visual.ColumnToX(col);
            var h = lineH * 0.8;
            var y = rowsTop + visual.RowOf(col) * lineH + (lineH - h) / 2;
            context.FillRectangle(EditorTheme.Caret, new Rect(caretX, y, 1.5, h));
        }

        return visual.Width;
    }

    /// <summary>Draw one visible run (content, or a revealed marker — markers carry their own brush in the style).</summary>
    private void DrawTextRun(DrawingContext context, VisualRun run, double lineTop, double lineH)
    {
        var ft = _measurer.GetFormattedText(run.Text, run.Style, run.Style.Foreground);
        var y = lineTop + (lineH - ft.Height) / 2;

        // An inline-code chip: sized to the code run's own font, not the row, and bled a few pixels past the
        // text so it reads as a chip around the code rather than a highlight behind it.
        if (run.Style.Background is not null)
        {
            const double bleed = 3;
            var chipH = run.Style.FontSize * 1.5;
            var chip = new Rect(run.X - bleed, lineTop + (lineH - chipH) / 2, ft.Width + (bleed * 2), chipH);
            context.FillRectangle(run.Style.Background, chip, 4);
        }

        context.DrawText(ft, new Point(run.X, y));
        DrawRunDecorations(context, run, ft, y);
    }

    /// <summary>
    /// Draw the run's underline/strikethrough as real lines in the drawing context. Avalonia's
    /// <c>FormattedText</c> text decorations don't paint dependably across backends, so — like Cellestra's
    /// editor overlay — the lines are stroked by hand: a strikethrough through the x-height, an underline just
    /// below the baseline. Each line uses its decoration's stroke (link underline in the link colour, strike
    /// in the text colour), defaulting to the run's foreground.
    /// </summary>
    private static void DrawRunDecorations(DrawingContext context, VisualRun run, FormattedText ft, double textTop)
    {
        if (run.Style.Decorations is not { } decorations || run.Text.Length == 0)
        {
            return;
        }

        var baseline = textTop + ft.Baseline;
        var x0 = run.X;
        var x1 = run.X + ft.Width;
        foreach (var d in decorations)
        {
            var stroke = d.Stroke ?? run.Style.Foreground;
            var pen = new Pen(stroke, Math.Max(1, run.Style.FontSize / 14.0));
            var lineY = d.Location == TextDecorationLocation.Strikethrough
                ? baseline - run.Style.FontSize * 0.26   // through the glyphs
                : baseline + Math.Max(1, run.Style.FontSize * 0.08); // just under the baseline
            context.DrawLine(pen, new Point(x0, lineY), new Point(x1, lineY));
        }
    }

    private void DrawSearchMatches(DrawingContext context, int blockStart, double blockTop, double lineH, VisualLine visual)
    {
        var blockEnd = blockStart + visual.RawLength;
        // Matches are sorted, non-overlapping → both Start and End ascend. Binary-search the first
        // match that could touch this block, then walk forward, so a block only touches its own matches
        // (not all of them) — O(log M + matches-in-block) instead of O(M) per block.
        for (var i = FirstMatchReaching(blockStart); i < _searchMatches.Count; i++)
        {
            var m = _searchMatches[i];
            if (m.Start >= blockEnd)
            {
                break; // all later matches start past this block
            }

            var s = Math.Clamp(m.Start - blockStart, 0, visual.RawLength);
            var e = Math.Clamp(m.End - blockStart, 0, visual.RawLength);
            if (e <= s)
            {
                continue;
            }

            var brush = i == _currentMatch ? EditorTheme.SearchMatchCurrent : EditorTheme.SearchMatch;
            foreach (var (row, x1, x2) in visual.Spans(s, e))
            {
                context.FillRectangle(brush, new Rect(x1, blockTop + row * lineH + 2, x2 - x1, lineH - 4), 2);
            }
        }
    }

    /// <summary>Index of the first match whose end is past <paramref name="lineStart"/> (lower bound).</summary>
    internal int FirstMatchReaching(int lineStart)
    {
        int lo = 0, hi = _searchMatches.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (_searchMatches[mid].End <= lineStart)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void DrawSelection(DrawingContext context, int blockStart, double blockTop, double lineH, Selection sel, VisualLine visual)
    {
        var contentLen = visual.RawLength;
        var blockEnd = blockStart + contentLen;
        if (sel.Start > blockEnd || sel.End < blockStart)
        {
            return; // selection doesn't touch this block
        }

        var selStartCol = Math.Clamp(sel.Start - blockStart, 0, contentLen);
        var selEndCol = Math.Clamp(sel.End - blockStart, 0, contentLen);
        if (selEndCol <= selStartCol)
        {
            return;
        }

        foreach (var (row, x1, x2) in visual.Spans(selStartCol, selEndCol))
        {
            context.FillRectangle(EditorTheme.SelectionBackground, new Rect(x1, blockTop + row * lineH + 2, x2 - x1, lineH - 4));
        }
    }

    // ----- input -----

    private void DrawDecoration(DrawingContext context, VisualRun run, double lineTop, double lineH)
    {
        var cy = lineTop + lineH / 2;
        switch (run.Decoration)
        {
            case DecorationKind.Bullet:
                {
                    var r = Math.Max(2, EditorTheme.BaseFontSize * 0.14);
                    context.DrawEllipse(EditorTheme.BulletForeground, null, new Point(run.X + run.Width / 2, cy), r, r);
                    break;
                }

            case DecorationKind.CheckboxUnchecked:
            case DecorationKind.CheckboxChecked:
                {
                    var size = CheckboxBoxSize;
                    var box = new Rect(run.X + CheckboxLeftPad, cy - size / 2, size, size);
                    var ticked = run.Decoration == DecorationKind.CheckboxChecked;
                    context.DrawRectangle(ticked ? EditorTheme.CheckboxForeground : null, new Pen(EditorTheme.CheckboxForeground, 1.4), box, 3, 3);
                    if (ticked)
                    {
                        // A check mark in the editor background colour so it reads on the filled box.
                        var pen = new Pen(EditorTheme.Background, 1.6);
                        var a = new Point(box.X + size * 0.24, cy);
                        var b = new Point(box.X + size * 0.43, cy + size * 0.22);
                        var c = new Point(box.X + size * 0.78, cy - size * 0.24);
                        context.DrawLine(pen, a, b);
                        context.DrawLine(pen, b, c);
                    }

                    break;
                }

            case DecorationKind.OrderedNumber:
                {
                    // The computed ordinal (e.g. "3. "), drawn in the bullet colour where the digits would be.
                    var ft = _measurer.GetFormattedText(run.Text, run.Style, EditorTheme.BulletForeground);
                    var ny = lineTop + (lineH - ft.Height) / 2;
                    context.DrawText(ft, new Point(run.X, ny));
                    break;
                }

            case DecorationKind.QuoteBar:
                // Nothing to draw: the bar is painted once for the whole quote by DrawBlockPanels, so it stays
                // continuous across the quote's inner padding and across wrapped continuation rows. The
                // decoration survives only for its width — it is what holds the quote's text off the bar.
                break;

            case DecorationKind.HorizontalRule:
                context.DrawLine(new Pen(EditorTheme.RuleForeground, EditorTheme.RuleThickness), new Point(run.X, cy), new Point(ContentRight(), cy));
                break;
        }
    }
}
