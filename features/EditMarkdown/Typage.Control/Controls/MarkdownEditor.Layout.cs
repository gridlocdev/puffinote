using Avalonia;
using Avalonia.Controls.Primitives;
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
            : Math.Max(EditorTheme.LineHeight, ContentHeight);
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
        if (!WordWrap)
        {
            return 0;
        }

        var w = _viewport.Width > 0 ? _viewport.Width : Bounds.Width;
        return w > 0 ? Math.Max(40, ContentWidth()) : 0;
    }

    // ----- rendering -----

    /// <summary>
    /// The leading front-matter block (PRD §7.4): line 0's opening <c>---</c> through its closing
    /// fence, or null when the document has none. Front matter exists only when a closing fence is
    /// present (a lone <c>---</c> tokenizes as a horizontal rule), so a found opening implies a close.
    /// </summary>
    private (int Start, int End)? FrontMatterBlock()
    {
        var doc = Buffer.Document;
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

    /// <summary>
    /// Test seam: step the caret's blink one phase, as its timer would. A headless test has no wall clock
    /// to wait on, so this is how it observes the caret going dark and coming back.
    /// </summary>
    internal void AdvanceCaretBlinkForTest() => _caretBlink.Advance();

    /// <summary>Test seam: whether the blink is currently on its lit phase.</summary>
    internal bool IsCaretLitForTest => _caretBlink.IsCaretVisible;

    /// <summary>Test seam: whether the blink's timer is running — the caret is blinking, not just lit.</summary>
    internal bool IsCaretBlinkingForTest => _caretBlink.IsBlinking;

    /// <summary>The global visual row the caret is actually drawn on, mirroring RenderBlock's placement; for tests.</summary>
    internal int CaretDrawnRowForTest()
    {
        EnsureLayout();
        var sel = Buffer.Selection;
        var caretBlock = ResolveCaret(sel.Caret).Block;
        var blockStart = BlockStartOffset(caretBlock);
        var visual = BlockVisual(caretBlock);
        var col = Math.Clamp(sel.Caret - blockStart, 0, visual.RawLength);
        return _rowPrefix[caretBlock] + visual.RowOf(col);
    }
}
