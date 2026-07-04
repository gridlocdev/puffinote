using Avalonia;
using Typage.Control.Rendering;
using Typage.Engine.Document;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    /// <summary>Scroll so the caret stays visible after an edit or navigation (we are the scrollable).</summary>
    private void ScrollCaretIntoView()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0)
        {
            return; // not arranged in a viewport yet (e.g. headless construction)
        }

        EnsureLayout();
        var r = ResolveCaret(Buffer.Selection.Caret);
        var x = r.Visual.ColumnToX(r.Col); // through the offset↔visual map, so hidden markers don't skew the scroll
        if (!WordWrap)
        {
            _extentWidth = Math.Max(_extentWidth, x + 12); // ensure we can scroll to a caret on a wide line
        }

        // Caret position in scroll-content space (which includes the padding), on its wrapped row.
        var px = x + PaddingLeft;
        var py = RowTop(r.Block) + r.Visual.RowOf(r.Col) * EditorTheme.LineHeight + PaddingTop;
        const double margin = 8;

        var newY = _offset.Y;
        if (py < _offset.Y)
        {
            newY = py;
        }
        else if (py + EditorTheme.LineHeight > _offset.Y + _viewport.Height)
        {
            newY = py + EditorTheme.LineHeight - _viewport.Height;
        }

        var newX = _offset.X;
        if (px < _offset.X + margin)
        {
            newX = Math.Max(0, px - margin);
        }
        else if (px + margin > _offset.X + _viewport.Width)
        {
            newX = px + margin - _viewport.Width;
        }

        var target = ClampOffset(new Vector(newX, newY));
        if (target != _offset)
        {
            _offset = target;
            InvalidateVisual();
            RaiseScrollInvalidated(EventArgs.Empty);
        }
    }

    private void MoveCaret(int newCaret, bool extend, bool keepGoalColumn = false)
    {
        newCaret = Math.Clamp(newCaret, 0, Buffer.Document.Length);
        var anchor = extend ? Buffer.Selection.Anchor : newCaret;
        Buffer.Selection = new Selection(anchor, newCaret);
        if (!keepGoalColumn)
        {
            _goalX = double.NaN; // a horizontal move resets the remembered column
        }
    }

    /// <summary>
    /// One caret step in <paramref name="dir"/> (+1 right, -1 left) that treats hidden markers as atomic: it
    /// glides over a concealed marker run, and over the <i>junction between two adjacent</i> concealed runs
    /// (a column with a hidden character on both sides — e.g. between the <c>***</c> and <c>~~</c> of
    /// <c>***~~bold~~***</c>, or between a link's <c>](</c> and its concealed URL), which is zero-width and
    /// visually identical to its neighbours, so stopping there would be a caret press that doesn't move. It
    /// still stops next to any visible glyph (so the position just inside/outside a construct is reachable)
    /// and over a collapsed blank separator (one \n\n = one step), reaching a real stop like a normal
    /// document editor (PRD §7.4).
    /// </summary>
    private int VisualStep(int caret, int dir)
    {
        var doc = Buffer.Document;
        var next = Math.Clamp(dir > 0 ? Buffer.NextCaret(caret) : Buffer.PreviousCaret(caret), 0, doc.Length);
        var r = ResolveCaret(next);
        var guard = 0;
        while ((IsBetweenHidden(r.Visual, r.Col) || IsCollapsedOffset(next)) && guard++ <= doc.Length)
        {
            var stepped = Math.Clamp(dir > 0 ? Buffer.NextCaret(next) : Buffer.PreviousCaret(next), 0, doc.Length);
            if (stepped == next)
            {
                break;
            }

            next = stepped;
            r = ResolveCaret(next);
        }

        return next;
    }

    /// <summary>
    /// The document offset one word away from <paramref name="offset"/> in <paramref name="dir"/> (+1
    /// right, -1 left): skip a run of whitespace, then a run of like-classed characters (word vs
    /// punctuation), the way Ctrl/⌥ + arrow works in every editor. Word boundaries fall on punctuation-run
    /// edges, which coincide with hidden marker edges, so the result is always a valid caret stop.
    /// </summary>
    private int WordBoundary(int offset, int dir)
    {
        var doc = Buffer.Document;
        var i = Math.Clamp(offset, 0, doc.Length);
        if (dir > 0)
        {
            while (i < doc.Length && char.IsWhiteSpace(doc[i]))
            {
                i++;
            }

            if (i < doc.Length)
            {
                var cls = CharClass(doc[i]);
                while (i < doc.Length && !char.IsWhiteSpace(doc[i]) && CharClass(doc[i]) == cls)
                {
                    i++;
                }
            }
        }
        else
        {
            while (i > 0 && char.IsWhiteSpace(doc[i - 1]))
            {
                i--;
            }

            if (i > 0)
            {
                var cls = CharClass(doc[i - 1]);
                while (i > 0 && !char.IsWhiteSpace(doc[i - 1]) && CharClass(doc[i - 1]) == cls)
                {
                    i--;
                }
            }
        }

        return i;
    }

    /// <summary>Delete from the start of the caret's visual row to the caret (macOS ⌘⌫).</summary>
    private void DeleteToRowStart(int caret)
    {
        if (!Buffer.Selection.IsEmpty)
        {
            Buffer.Insert(string.Empty);
            return;
        }

        var start = RowStartOffset(caret);
        if (start < caret)
        {
            Buffer.ReplaceRange(start, caret - start, string.Empty);
        }
    }

    /// <summary>Delete from the caret to the next word boundary in <paramref name="dir"/> (Ctrl/⌥ + Backspace/Delete).</summary>
    private void DeleteWord(int dir)
    {
        var sel = Buffer.Selection;
        if (!sel.IsEmpty)
        {
            Buffer.Insert(string.Empty); // a selection deletes normally
            return;
        }

        var target = WordBoundary(sel.Caret, dir);
        if (target == sel.Caret)
        {
            return;
        }

        var from = Math.Min(sel.Caret, target);
        Buffer.ReplaceRange(from, Math.Abs(target - sel.Caret), string.Empty);
    }

    /// <summary>The source-column range <c>[start, end)</c> occupied by visual <paramref name="row"/> of <paramref name="visual"/>.</summary>
    private static (int Start, int End) RowColumns(VisualLine visual, int row)
    {
        var start = int.MaxValue;
        var end = 0;
        foreach (var run in visual.Runs)
        {
            if (run.Len > 0 && run.Row == row)
            {
                start = Math.Min(start, run.Col);
                end = Math.Max(end, run.Col + run.Len);
            }
        }

        return start == int.MaxValue ? (0, 0) : (start, end);
    }

    /// <summary>The offset at the start of the caret's wrapped visual row.</summary>
    private int RowStartOffset(int caret)
    {
        var r = ResolveCaret(caret);
        return r.BlockStart + RowColumns(r.Visual, r.Visual.RowOf(r.Col)).Start;
    }

    /// <summary>The offset at the end of the caret's wrapped visual row.</summary>
    private int RowEndOffset(int caret)
    {
        var r = ResolveCaret(caret);
        return r.BlockStart + RowColumns(r.Visual, r.Visual.RowOf(r.Col)).End;
    }

    /// <summary>
    /// Smart Home: jump to the first visible, non-whitespace content on the caret's visual row; when the
    /// caret is already there, jump to the true start of the row instead (the toggle every editor has).
    /// </summary>
    private int SmartHome(int caret)
    {
        var doc = Buffer.Document;
        var r = ResolveCaret(caret);
        var (start, end) = RowColumns(r.Visual, r.Visual.RowOf(r.Col));
        var content = start;
        while (content < end &&
               (r.Visual.IsColumnHidden(content) || char.IsWhiteSpace(doc[r.BlockStart + content])))
        {
            content++;
        }

        var contentOffset = r.BlockStart + content;
        return caret == contentOffset ? r.BlockStart + start : contentOffset;
    }

    /// <summary>The number of visual rows a Page Up/Down moves (a viewport-height of rows, less one for overlap).</summary>
    private int PageRows()
    {
        var h = _viewport.Height > 0 ? _viewport.Height : Bounds.Height;
        return Math.Max(1, (int)(h / EditorTheme.LineHeight) - 1);
    }

    /// <summary>
    /// Backspace in WYSIWYG mode: delete the previous <b>visible</b> grapheme, stepping over any concealed
    /// marker punctuation between it and the caret so hidden markers stay balanced (backspacing at the end
    /// of a bold word removes a letter, not an invisible <c>*</c>). When only hidden markers precede the
    /// caret on the line, the whole marker prefix is removed instead (un-list / un-quote / un-heading the
    /// line). Returns false at a true line start so the buffer's plain backspace can join lines.
    /// </summary>
    private bool TryVisualBackspace()
    {
        var sel = Buffer.Selection;
        if (!sel.IsEmpty)
        {
            return false; // a selection deletes normally
        }

        var doc = Buffer.Document;
        var r = ResolveCaret(sel.Caret);
        if (r.Col == 0)
        {
            return false; // block start: let plain backspace join with the previous block
        }

        // A line that holds nothing but indentation (tabs/spaces): one backspace clears the whole line and
        // drops the caret to the line start, instead of nibbling one whitespace char at a time. Only the
        // whitespace is removed — a trailing '\r' (CRLF) is preserved so line endings stay byte-for-byte.
        var caretLine = doc.GetPosition(sel.Caret).Line;
        var lineStart = doc.GetLineStart(caretLine);
        var lineText = doc.GetText(lineStart, doc.GetLineEnd(caretLine) - lineStart);
        var content = lineText.EndsWith('\r') ? lineText[..^1] : lineText;
        if (content.Length > 0 && content.Trim().Length == 0)
        {
            Buffer.ReplaceRange(lineStart, content.Length, string.Empty);
            Buffer.Selection = Selection.At(lineStart);
            return true;
        }

        if (TryRestoreBlankLineByRemovingSyntheticParagraphChar(caretLine, sel.Caret, forward: false))
        {
            return true;
        }

        var col = r.Col;
        while (col > 0 && r.Visual.IsColumnHidden(col - 1))
        {
            col--;
        }

        // If the caret sits just past a hidden marker run (col < r.Col) and only indentation precedes that
        // run, the caret is at a list/heading/quote content-start: strip the marker itself, keeping any
        // leading indentation. So backspace at the right of a (possibly nested) bullet / number / checkbox
        // removes the marker rather than nibbling an indent space. col == 0 is the un-indented case.
        if (col < r.Col && doc.GetText(r.BlockStart, col).Trim().Length == 0)
        {
            Buffer.ReplaceRange(r.BlockStart + col, r.Col - col, string.Empty);
            Buffer.Selection = Selection.At(r.BlockStart + col);
            return true;
        }

        // Grapheme boundaries are taken over the real document text (interior newlines included), so a
        // backspace at a soft-joined line boundary deletes the joining newline and merges the lines.
        var realText = doc.GetText(r.BlockStart, BlockEndOffset(r.Block) - r.BlockStart);
        var start = Grapheme.PreviousBoundary(realText, col);
        Buffer.ReplaceRange(r.BlockStart + start, col - start, string.Empty);
        Buffer.Selection = Selection.At(r.BlockStart + start);
        return true;
    }

    /// <summary>
    /// Forward Delete in WYSIWYG mode: delete the next <b>visible</b> grapheme, stepping over concealed
    /// marker punctuation ahead of the caret. Returns false when only hidden markers (or end-of-line)
    /// follow, letting the buffer's plain delete handle the line join.
    /// </summary>
    private bool TryVisualDelete()
    {
        var sel = Buffer.Selection;
        if (!sel.IsEmpty)
        {
            return false;
        }

        var doc = Buffer.Document;
        var caretLine = doc.GetPosition(sel.Caret).Line;
        if (TryRestoreBlankLineByRemovingSyntheticParagraphChar(caretLine, sel.Caret, forward: true))
        {
            return true;
        }

        var r = ResolveCaret(sel.Caret);
        var col = r.Col;
        while (col < r.Visual.RawLength && r.Visual.IsColumnHidden(col))
        {
            col++;
        }

        if (col >= r.Visual.RawLength)
        {
            return false; // nothing visible ahead in this block: let plain delete join the next block
        }

        var realText = doc.GetText(r.BlockStart, BlockEndOffset(r.Block) - r.BlockStart);
        var end = Grapheme.NextBoundary(realText, col);
        Buffer.ReplaceRange(r.BlockStart + col, end - col, string.Empty);
        Buffer.Selection = Selection.At(r.BlockStart + col);
        return true;
    }

    /// <summary>
    /// Typing into a visible blank line above a paragraph materializes that line as <c>text + newline</c> so
    /// the following paragraph keeps its own break. If the user then deletes the line's <i>only</i> grapheme,
    /// remove that supporting newline as well and restore the original visible blank line instead of leaving
    /// an extra empty paragraph behind.
    /// </summary>
    private bool TryRestoreBlankLineByRemovingSyntheticParagraphChar(int line, int caret, bool forward)
    {
        var doc = Buffer.Document;
        if (line + 2 >= doc.LineCount || !IsCollapsedBlankLine(line + 1) || IsBlankLine(line) || IsBlankLine(line + 2))
        {
            return false;
        }

        var lineStart = doc.GetLineStart(line);
        var lineEnd = ContentEnd(line);
        var text = doc.GetText(lineStart, lineEnd - lineStart);
        if (text.Length == 0 || Grapheme.NextBoundary(text, 0) != text.Length)
        {
            return false; // only a single-grapheme line is eligible for the round-trip restore
        }

        if ((!forward && caret != lineEnd) || (forward && caret != lineStart))
        {
            return false;
        }

        var removeStart = forward ? lineStart : lineStart;
        var removeEnd = doc.GetLineStart(line + 1); // delete the char plus its supporting newline
        Buffer.ReplaceRange(removeStart, removeEnd - removeStart, string.Empty);
        Buffer.Selection = Selection.At(lineStart);
        return true;
    }

    /// <summary>Offset just past <paramref name="line"/>'s last content character, also dropping a trailing
    /// <c>\r</c> (which <see cref="TextDocument.GetLineEnd"/> keeps as content) — so a CRLF merge removes the
    /// whole <c>\r\n</c> rather than stranding a bare carriage return.</summary>
    private int ContentEnd(int line)
    {
        var doc = Buffer.Document;
        var end = doc.GetLineEnd(line);
        return end > doc.GetLineStart(line) && doc[end - 1] == '\r' ? end - 1 : end;
    }

    /// <summary>
    /// Backspace at the start of a paragraph that sits under a collapsed blank separator: remove the whole
    /// <c>\n\n</c> break (not just one newline, which would leave a single soft-break newline that flows the
    /// paragraphs back together), merging this paragraph onto the end of the one above. Mirrors the Enter that
    /// created the break.
    /// </summary>
    private bool TryBackspaceCollapsedBlank()
    {
        var sel = Buffer.Selection;
        if (!sel.IsEmpty)
        {
            return false;
        }

        var doc = Buffer.Document;
        var caret = sel.Caret;
        var line = doc.GetPosition(caret).Line;
        if (caret != doc.GetLineStart(line) || line < 1 || !IsCollapsedBlankLine(line - 1))
        {
            return false;
        }

        // Only merge when this line is real content. On a *visible* empty line (between paragraphs) a plain
        // backspace removes one newline — which is exactly what collapses two blanks back to one — so don't
        // grab the whole \n\n here, which would re-flow the paragraphs above and below into one.
        if (IsBlankLine(line))
        {
            return false;
        }

        var from = ContentEnd(line - 2); // end of the content line just above the blank (excluding its \r)
        Buffer.ReplaceRange(from, caret - from, string.Empty);
        Buffer.Selection = Selection.At(from);
        return true;
    }

    /// <summary>Forward Delete at the end of a paragraph followed by a collapsed blank separator: remove the
    /// whole <c>\n\n</c> break, pulling the next paragraph up (the symmetric counterpart of backspace).</summary>
    private bool TryDeleteCollapsedBlank()
    {
        var sel = Buffer.Selection;
        if (!sel.IsEmpty)
        {
            return false;
        }

        var doc = Buffer.Document;
        var caret = sel.Caret;
        var line = doc.GetPosition(caret).Line;
        // Compare against the visible content end (which excludes a CRLF's \r) — that's where End/⌘-Right put
        // the caret, so forward-delete merges symmetrically with backspace on CRLF as well as LF.
        if (caret != ContentEnd(line) || !IsCollapsedBlankLine(line + 1))
        {
            return false;
        }

        var to = doc.GetLineStart(line + 2); // start of the content line just below the blank
        Buffer.ReplaceRange(caret, to - caret, string.Empty);
        Buffer.Selection = Selection.At(caret);
        return true;
    }

    /// <summary>
    /// Move the caret up/down by <paramref name="delta"/> lines, holding a <b>sticky goal column</b>:
    /// the caret's x is remembered on the first vertical move and reused, so passing through shorter
    /// lines doesn't permanently snap the column inward (the behavior every editor has).
    /// </summary>
    /// <summary>
    /// A bare Up/Down arrow. With a range selection and no Shift, it collapses to the selection's edge —
    /// Up to the top (start), Down to the bottom (end) — rather than stepping a visual row from the active
    /// caret; so e.g. Up right after Select All returns to the top of the selection. With Shift (extend) or
    /// no selection it moves by one visual row as usual.
    /// </summary>
    private void ArrowByLine(int delta, bool extend)
    {
        var sel = Buffer.Selection;
        if (!extend && !sel.IsEmpty)
        {
            MoveCaret(delta < 0 ? sel.Start : sel.End, extend: false);
            return;
        }

        MoveCaretByLine(delta, extend);
    }

    private void MoveCaretByLine(int delta, bool extend)
    {
        EnsureLayout();
        var r = ResolveCaret(Buffer.Selection.Caret);
        if (double.IsNaN(_goalX))
        {
            _goalX = r.Visual.ColumnToX(r.Col);
        }

        // Move by one wrapped visual row, not a whole document line, so Up/Down step through a long
        // wrapped paragraph (and across joined source lines) the way a normal document editor does.
        var globalRow = _rowPrefix[r.Block] + r.Visual.RowOf(r.Col);
        var requestedRow = globalRow + delta;

        // Off the top/bottom edge: Up on the first visual row jumps to the very start of the document,
        // Down on the last visual row jumps to the very end — like a normal editor, rather than sitting
        // still on the same row at the goal column.
        if (requestedRow < 0)
        {
            MoveCaret(0, extend);
            return;
        }

        if (requestedRow > TotalRows - 1)
        {
            MoveCaret(Buffer.Document.Length, extend);
            return;
        }

        var targetRow = requestedRow;
        var targetBlock = BlockAtRow(targetRow);
        var localRow = targetRow - _rowPrefix[targetBlock];
        MoveCaret(OffsetAt(targetBlock, localRow, _goalX), extend, keepGoalColumn: true);
    }
}
