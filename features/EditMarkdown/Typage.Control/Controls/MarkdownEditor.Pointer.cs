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

        // A secondary click on a task's checkbox opens its status picker: the primary click is spent on the one
        // action worth a click (complete it), so the four states in between hang off the click that means
        // "what else could this be?" — dropped at the box itself.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            var at = e.GetPosition(this);
            var lamp = TaskLampAt(Buffer.Document.GetPosition(OffsetAtPoint(at)).Line, at);
            if (lamp >= 0)
            {
                Focus();
                ShowTaskStatusPicker(lamp);
                e.Handled = true;
                return;
            }
        }

        // Let any other right-click through so the context menu can open; only the left button moves the caret.
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
    /// Toggle a task checkbox hit by a click on <paramref name="line"/>: first the drawn checkbox lamp
    /// (when the marker is concealed), then the raw <c>[ ]</c> at <paramref name="offset"/> (caret line).
    ///
    /// <para>A plain click <b>completes</b> — it is the one task action worth a single click, and the one people
    /// reach for a hundred times a day. The other four states are chosen deliberately, from the picker a
    /// secondary click opens (<see cref="OpenTaskStatusPicker"/>).</para>
    /// </summary>
    private bool TryToggleTaskClick(int line, Point point, int offset)
    {
        var hit = TaskLampAt(line, point);
        return hit >= 0 ? ToggleTaskOnLine(hit) : TryToggleTaskAt(line, offset);
    }

    /// <summary>
    /// The line whose drawn checkbox lamp <paramref name="point"/> lands on (−1 if none) — the hit-test both the
    /// click-to-complete and the status picker read, so the two can never disagree about where the box is.
    /// </summary>
    private int TaskLampAt(int line, Point point)
    {
        // A task line carries a list marker, so it's always a singleton block (block start == line start).
        var block = BlockOf(line);
        var visual = BlockVisual(block);
        var contentX = point.X - ContentLeft() + _offset.X;
        var blockStart = BlockStartOffset(block);
        foreach (var run in visual.Runs)
        {
            if (run.Decoration == DecorationKind.Checkbox && contentX >= run.X && contentX <= run.X + run.Width)
            {
                // The box hangs on the task marker's columns on a bulleted item but on the *list* marker's on a
                // numbered one (where it leads the number), so answer with the line the run sits on — a line has
                // at most one live checkbox — rather than with the run's own columns.
                return Buffer.Document.GetPosition(blockStart + run.Col).Line;
            }
        }

        return -1;
    }

    /// <summary>
    /// If <paramref name="offset"/> lands on a task-list checkbox on <paramref name="line"/>, toggle it
    /// (leaving the caret where it was) and return true (PRD §7.2).
    /// </summary>
    private bool TryToggleTaskAt(int line, int offset)
    {
        foreach (var t in _model.LineTokens(line))
        {
            if (t.Type != TokenType.TaskMarker || offset < t.Start || offset > t.Start + 3)
            {
                continue;
            }

            return ToggleTaskOnLine(line);
        }

        return false;
    }

    /// <summary>Toggle the first task checkbox on <paramref name="line"/> (used by ⌘L), if it has one: done —
    /// or, if it is already done, back to not started (<see cref="TaskStates.Toggled"/>).</summary>
    private bool ToggleTaskOnLine(int line) =>
        TaskStateOf(line) is { } state && SetTaskStateOnLine(line, TaskStates.Toggled(state));

    /// <summary>The state of <paramref name="line"/>'s task box, or null when the line carries no task.</summary>
    private TaskState? TaskStateOf(int line)
    {
        foreach (var t in _model.LineTokens(line))
        {
            if (t.Type == TokenType.TaskMarker)
            {
                return TaskStates.FromMark(t.Meta is { Length: > 0 } mark ? mark[0] : ' ');
            }
        }

        return null;
    }

    /// <summary>
    /// Write <paramref name="state"/> into <paramref name="line"/>'s task box — the <b>one</b> place a task's
    /// state is written, whether the change came from a click, ⌘L, or the picker. It rewrites the single
    /// character between the brackets and leaves the caret put; the rest of the line is untouched, so the
    /// document's bytes change exactly where the reader said they should (golden rule 5).
    /// </summary>
    private bool SetTaskStateOnLine(int line, TaskState state)
    {
        var box = -1;
        foreach (var t in _model.LineTokens(line))
        {
            if (t.Type == TokenType.TaskMarker)
            {
                box = t.Start + 1; // the character between the brackets
                break;
            }
        }

        if (box < 0 || box >= Buffer.Document.Length)
        {
            return false;
        }

        if (Buffer.Document[box] == TaskStates.MarkOf(state))
        {
            return true; // already there — don't push an empty edit onto the undo stack
        }

        var caret = Buffer.Selection;
        Buffer.ReplaceRange(box, 1, TaskStates.MarkText(state));
        if (!TrySortCompletedTaskGroup(line, caret))
        {
            Buffer.Selection = caret; // changing a checkbox shouldn't move the caret
        }

        return true;
    }

    /// <summary>Map a pointer position to the nearest caret offset (accounting for padding, scroll, and wrapped rows).</summary>
    private int OffsetAtPoint(Point point)
    {
        EnsureLayout();
        var contentY = point.Y - PaddingTop + _offset.Y;
        var contentX = point.X - ContentLeft() + _offset.X;

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
