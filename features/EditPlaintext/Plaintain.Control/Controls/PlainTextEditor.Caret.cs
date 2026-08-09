using Avalonia;
using Puffinote.Text.Document;

namespace Plaintain.Control.Controls;

public sealed partial class PlainTextEditor
{
    /// <summary>
    /// The "goal column" a vertical move aims for. Walking down past a short line and back should
    /// return to the column you started in, not to the short line's end — so the column is remembered
    /// across a run of ↑/↓ presses and cleared by anything else.
    /// </summary>
    private int? _goalColumn;

    /// <summary>The caret's visual row and expanded column.</summary>
    internal (int Row, int Column) CaretRowColumn => RowColumnOf(Buffer.Selection.Caret);

    internal (int Row, int Column) RowColumnOf(int offset)
    {
        var doc = Buffer.Document;
        int line = doc.GetPosition(Math.Clamp(offset, 0, doc.Length)).Line;
        int column = ColumnOf(line, offset);
        if (!WordWrap)
        {
            return (line, column);
        }

        int segment = SegmentAtColumn(line, column);
        return (FirstRowOf(line) + segment, column - SegmentColumns(line, segment).Start);
    }

    /// <summary>The document offset at a visual row and expanded column (clamped into the row).</summary>
    internal int OffsetAt(int row, int column)
    {
        int rows = Math.Max(1, TotalRows);
        row = Math.Clamp(row, 0, rows - 1);
        var (line, segment) = LineAtRow(row);
        if (!WordWrap)
        {
            return OffsetAtColumn(line, column);
        }

        // On a folded row, never run past the break — the next column belongs to the row below.
        var (start, end) = SegmentColumns(line, segment);
        return OffsetAtColumn(line, Math.Min(start + column, end));
    }

    /// <summary>Move the caret, extending the selection when <paramref name="extend"/> (a Shift chord).</summary>
    internal void MoveCaret(int offset, bool extend)
    {
        // Moving the caret ends a multi-selection, as it does everywhere this gesture exists: the extra
        // carets belong to the occurrences ⌘D found, and once you have gone somewhere else they are no
        // longer about anything. (Escape takes them down deliberately; this is the accidental way out.)
        ClearExtraSelections();

        int clamped = Math.Clamp(offset, 0, Buffer.Document.Length);
        Buffer.Selection = extend
            ? Buffer.Selection with { Caret = clamped }
            : Selection.At(clamped);
        BringCaretIntoView();
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Step one visual row up or down, holding the goal column.</summary>
    internal void MoveByRow(int delta, bool extend)
    {
        var (row, column) = CaretRowColumn;
        _goalColumn ??= column;
        MoveCaret(OffsetAt(row + delta, _goalColumn.Value), extend);
        _goalColumn ??= column; // MoveCaret does not clear it; keep the run alive
    }

    /// <summary>Forget the goal column — any move that isn't ↑/↓ starts a new run.</summary>
    internal void ClearGoalColumn() => _goalColumn = null;

    /// <summary>The offset at the start of the caret's visual row (the row, not the line, when wrapping).</summary>
    internal int RowStartOffset()
    {
        var (row, _) = CaretRowColumn;
        return OffsetAt(row, 0);
    }

    /// <summary>The offset at the end of the caret's visual row.</summary>
    internal int RowEndOffset()
    {
        var (row, _) = CaretRowColumn;
        var (line, segment) = LineAtRow(row);
        return RowEndOffsetFor(line, segment);
    }

    /// <summary>
    /// "Smart Home": the first non-whitespace character on the row, and the row's true start if the
    /// caret is already there. Indented text is what you usually mean by "the beginning of the line".
    /// </summary>
    internal int SmartHomeOffset()
    {
        var doc = Buffer.Document;
        int start = RowStartOffset();
        int end = RowEndOffset();
        int content = start;
        while (content < end && (doc[content] == ' ' || doc[content] == '\t'))
        {
            content++;
        }

        return Buffer.Selection.Caret == content ? start : content;
    }

    /// <summary>The offset one word boundary away, in <paramref name="direction"/> (-1 or +1).</summary>
    internal int WordBoundary(int offset, int direction)
    {
        var doc = Buffer.Document;
        int i = Math.Clamp(offset, 0, doc.Length);
        if (direction < 0)
        {
            while (i > 0 && IsWordSeparator(doc[i - 1]))
            {
                i--;
            }

            while (i > 0 && !IsWordSeparator(doc[i - 1]))
            {
                i--;
            }

            return i;
        }

        while (i < doc.Length && !IsWordSeparator(doc[i]))
        {
            i++;
        }

        while (i < doc.Length && IsWordSeparator(doc[i]) && doc[i] != '\n')
        {
            i++;
        }

        return i;
    }

    private static bool IsWordSeparator(char c) => !char.IsLetterOrDigit(c) && c != '_';

    /// <summary>Rows that fit in the viewport — one Page Up/Down.</summary>
    internal int PageRows() => Math.Max(1, (int)(Viewport.Height / _rowHeight) - 1);

    /// <summary>Scroll the caret into view, vertically and (when not wrapping) horizontally.</summary>
    internal void BringCaretIntoView()
    {
        if (Viewport.Height <= 0)
        {
            return;
        }

        var (row, column) = CaretRowColumn;
        double y = row * _rowHeight;
        double x = _offset.X;
        double top = _offset.Y;

        if (y < top)
        {
            top = y;
        }
        else if (y + _rowHeight > top + Viewport.Height)
        {
            top = y + _rowHeight - Viewport.Height;
        }

        if (!WordWrap)
        {
            double caretX = column * _charWidth;
            double visible = Math.Max(0, Viewport.Width - TextLeft - PadX);
            if (caretX < x)
            {
                x = caretX;
            }
            else if (caretX + _charWidth > x + visible)
            {
                x = caretX + _charWidth - visible;
            }
        }
        else
        {
            x = 0;
        }

        Offset = new Vector(x, top);
    }

    /// <summary>Scroll <paramref name="line"/> to the middle of the viewport — Go to Line's landing.</summary>
    public void RevealLine(int line)
    {
        int clamped = Math.Clamp(line, 0, Math.Max(0, Buffer.Document.LineCount - 1));
        MoveCaret(Buffer.Document.GetLineStart(clamped), extend: false);
        Offset = new Vector(_offset.X, Math.Max(0, (FirstRowOf(clamped) * _rowHeight) - (Viewport.Height / 2)));
        InvalidateVisual();
    }
}
