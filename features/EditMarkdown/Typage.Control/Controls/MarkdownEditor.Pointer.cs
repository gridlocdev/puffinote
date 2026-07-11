using Avalonia;
using Avalonia.Input;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ClearReorderIndicator(); // a click ends the reorder session

        // A click on a code block's copy button copies that block and shows the confirmation — handled
        // before caret placement so it doesn't also move the caret.
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var p = e.GetPosition(this);
            foreach (var btn in _codeCopyButtons)
            {
                if (btn.Rect.Contains(p))
                {
                    CopyCodeBlock(btn.First, btn.Last);
                    e.Handled = true;
                    return;
                }
            }
        }

        // Let a right-click through so the context menu can open; only the left button moves the caret.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        // Resolve the click to a document offset (hit-testing handles padding, scroll, and wrapped rows),
        // then take its document line for word-select / checkbox hit-testing.
        var offset = OffsetAtPoint(point);
        var line = Buffer.Document.GetPosition(offset).Line;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // A double-click selects the word (or run) under the cursor; a triple-click selects the whole
        // paragraph block (the standard editor escalation).
        if (e.ClickCount == 2)
        {
            SelectWordAt(line, offset);
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        if (e.ClickCount >= 3)
        {
            SelectBlockAt(line);
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        // A click on a task-list checkbox toggles it (PRD §7.2) rather than moving the caret — whether
        // it's the raw "[ ]" (caret line) or the drawn checkbox glyph (concealed line).
        if (!shift && TryToggleTaskClick(line, point, offset))
        {
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        MoveCaret(offset, shift);
        _dragging = true; // begin a drag-select; OnPointerMoved extends the selection
        e.Pointer.Capture(this);
        e.Handled = true;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            MoveCaret(OffsetAtPoint(e.GetPosition(this)), extend: true); // hold the anchor, move the caret
            e.Handled = true;
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        base.OnPointerReleased(e);
    }

    /// <summary>
    /// Select the run under <paramref name="offset"/> on <paramref name="line"/>: a word (letters /
    /// digits / underscore), a whitespace run, or a punctuation run — the standard double-click word
    /// select. Snaps the selection ends to grapheme boundaries so it never splits a cluster.
    /// </summary>
    private void SelectWordAt(int line, int offset)
    {
        var doc = Buffer.Document;
        var lineStart = doc.GetLineStart(line);
        var text = doc.GetText(lineStart, doc.GetLineEnd(line) - lineStart).TrimEnd('\r');
        if (text.Length == 0)
        {
            MoveCaret(lineStart, extend: false);
            return;
        }

        var col = Math.Clamp(offset - lineStart, 0, text.Length);
        var probe = col < text.Length ? col : col - 1;
        var cls = CharClass(text[probe]);

        var start = probe;
        while (start > 0 && CharClass(text[start - 1]) == cls)
        {
            start--;
        }

        var end = probe + 1;
        while (end < text.Length && CharClass(text[end]) == cls)
        {
            end++;
        }

        // Snap to grapheme boundaries (a surrogate pair / combining sequence stays whole).
        start = Grapheme.PreviousBoundary(text, Math.Min(start + 1, text.Length));
        end = Grapheme.NextBoundary(text, Math.Max(0, end - 1));
        Buffer.Selection = new Selection(lineStart + start, lineStart + end);
        _goalX = double.NaN;
    }

    /// <summary>Select the whole paragraph block containing <paramref name="line"/> (triple-click).</summary>
    private void SelectBlockAt(int line)
    {
        var block = BlockOf(line);
        Buffer.Selection = new Selection(BlockStartOffset(block), BlockEndOffset(block));
        _goalX = double.NaN;
    }

    /// <summary>
    /// Toggle a task checkbox hit by a click on <paramref name="line"/>: first the drawn checkbox glyph
    /// (when the marker is concealed), then the raw <c>[ ]</c> at <paramref name="offset"/> (caret line).
    /// </summary>
    private bool TryToggleTaskClick(int line, Point point, int offset)
    {
        // A task line carries a list marker, so it's always a singleton block (block start == line start).
        var block = BlockOf(line);
        var visual = BlockVisual(block);
        var contentX = point.X - PaddingLeft + _offset.X;
        var blockStart = BlockStartOffset(block);
        foreach (var run in visual.Runs)
        {
            if (run.Decoration is DecorationKind.CheckboxChecked or DecorationKind.CheckboxUnchecked
                && contentX >= run.X && contentX <= run.X + run.Width)
            {
                return TryToggleTaskAt(line, blockStart + run.Col); // run.Col is the task marker's column
            }
        }

        return TryToggleTaskAt(line, offset);
    }

    /// <summary>
    /// If <paramref name="offset"/> lands on a task-list checkbox on <paramref name="line"/>, flip it
    /// between <c>[ ]</c> and <c>[x]</c> (leaving the caret where it was) and return true (PRD §7.2).
    /// </summary>
    private bool TryToggleTaskAt(int line, int offset)
    {
        foreach (var t in _model.LineTokens(line))
        {
            if (t.Type != TokenType.TaskMarker || offset < t.Start || offset > t.Start + 3)
            {
                continue;
            }

            return ToggleBoxAt(line, t.Start + 1); // the character between the brackets
        }

        return false;
    }

    /// <summary>Flip the first task checkbox on <paramref name="line"/> (used by ⌘L), if it has one.</summary>
    private bool ToggleTaskOnLine(int line)
    {
        foreach (var t in _model.LineTokens(line))
        {
            if (t.Type == TokenType.TaskMarker)
            {
                return ToggleBoxAt(line, t.Start + 1);
            }
        }

        return false;
    }

    /// <summary>Flip the <c>[ ]</c>/<c>[x]</c> box character at <paramref name="box"/>, leaving the caret put.</summary>
    private bool ToggleBoxAt(int line, int box)
    {
        if (box < 0 || box >= Buffer.Document.Length)
        {
            return false;
        }

        var replacement = Buffer.Document[box] == ' ' ? "x" : " ";
        var caret = Buffer.Selection;
        Buffer.ReplaceRange(box, 1, replacement);
        if (!TrySortCompletedTaskGroup(line, caret))
        {
            Buffer.Selection = caret; // toggling a checkbox shouldn't move the caret
        }

        return true;
    }

    /// <summary>Map a pointer position to the nearest caret offset (accounting for padding, scroll, and wrapped rows).</summary>
    private int OffsetAtPoint(Point point)
    {
        EnsureLayout();
        var contentY = point.Y - PaddingTop + _offset.Y;
        var contentX = point.X - PaddingLeft + _offset.X;

        // Blocks have variable heights, so find the block by pixel and only then divide by *that* block's row
        // height. Dividing the whole document by one line height (as a uniform-row layout could) would drift
        // out of alignment the moment a heading or a code block changed the row pitch above the click.
        var block = BlockAtY(contentY);
        var localRow = (int)((contentY - RowTop(block)) / RowHeight(block));
        localRow = Math.Clamp(localRow, 0, Math.Max(0, _rowCounts[block] - 1));
        return OffsetAt(block, localRow, contentX);
    }

    // ----- helpers -----

    private int OffsetAt(int block, int row, double x)
    {
        // Hit-test against the block's visual map on the clicked wrapped row (concealed markers are
        // zero-width and never a caret stop), so a click resolves to the right document offset even where
        // markers are hidden, the paragraph is joined from several source lines, or the text wraps.
        var visual = BlockVisual(block);
        var offset = BlockStartOffset(block) + visual.XToColumn(row, x);

        // A CRLF soft-break inside a flow-joined paragraph renders as a column for the \r and one for the \n;
        // never let a click resolve strictly between them — an edit there would split the \r\n into a stray
        // bare \r. Snap to the content side (before the \r).
        var doc = Buffer.Document;
        if (offset > 0 && offset < doc.Length && doc[offset - 1] == '\r' && doc[offset] == '\n')
        {
            offset--;
        }

        return offset;
    }
}
