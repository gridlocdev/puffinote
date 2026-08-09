namespace Plaintain.Control.Controls;

public sealed partial class PlainTextEditor
{
    /// <summary>
    /// Where a folded line breaks: the expanded column each of its visual rows begins at.
    ///
    /// <para>This is the one place the surface decides <i>word</i> wrap rather than character wrap. Every
    /// other piece of geometry — the row map, the caret, the selection, the painted slice — asks only for
    /// these boundaries and stays the arithmetic it always was, because the segments <b>partition</b> a
    /// line's columns: row <c>i</c> covers <c>[Starts[i], Starts[i+1])</c> with no gap and no overlap, so
    /// every column still belongs to exactly one row and the offset↔row map stays reversible.</para>
    ///
    /// <para>Still no text layout: the break points come from walking the line's characters in the same
    /// expanded-column units <see cref="ColumnOf"/> counts in, never from measuring a glyph. That is what
    /// the monospace rule buys, and it is why folding a 50 MB log is a scan rather than a layout pass.</para>
    /// </summary>
    private readonly record struct LineWrap(int[] Starts, int Columns);

    /// <summary>A line that doesn't fold — one row, beginning at column 0. Shared, never mutated.</summary>
    private static readonly int[] OneRow = [0];

    // A one-line memo. Rendering asks the same line for its segment, its row end and its selection bounds
    // several times in a row, and the caret asks for it again on the next keystroke; recomputing the walk
    // each time would make every paint quadratic in the folded line's length for no reason. One entry is
    // enough because every caller works a line at a time, and it is keyed by the wrap width so a resize
    // can't serve boundaries measured against the old viewport.
    private int _wrapLine = -1;
    private int _wrapPerRow = -1;
    private LineWrap _wrap;

    /// <summary>
    /// The fold points of <paramref name="line"/>, computed once and remembered. Only ever asked while
    /// wrapping — with wrap off a line <i>is</i> a row, and nothing about it needs to be walked at all.
    /// </summary>
    private LineWrap WrapOf(int line)
    {
        int perRow = ColumnsPerRow;
        if (_wrapLine == line && _wrapPerRow == perRow)
        {
            return _wrap;
        }

        _wrap = ComputeWrap(line, perRow);
        _wrapLine = line;
        _wrapPerRow = perRow;
        return _wrap;
    }

    /// <summary>Forget the memoized line — the document or the wrap width moved under it.</summary>
    private void InvalidateWrap() => _wrapLine = -1;

    /// <summary>
    /// Walk <paramref name="line"/> and find its fold points, greedily: fill a row up to
    /// <paramref name="perRow"/> columns and, when the next word won't fit, start the row at that word
    /// instead of mid-word. A word longer than a whole row has nowhere better to break, so it falls back
    /// to breaking at the edge — which is what every editor does with a 300-character URL, and which is
    /// also why a document of unbroken text folds exactly as it did before this existed.
    ///
    /// <para>Trailing blanks stay on the row they end: whitespace running past the right edge is invisible,
    /// and pushing it down would leave a row ending early with the next one indented by nothing.</para>
    /// </summary>
    private LineWrap ComputeWrap(int line, int perRow)
    {
        var doc = Buffer.Document;
        int start = doc.GetLineStart(line);
        int end = doc.GetLineEnd(line);

        List<int>? folds = null; // allocated only for a line that actually folds — most never do
        int segStart = 0;        // the column the row being filled begins at
        int column = 0;
        int wordStart = -1;      // the last word boundary inside this row, or -1 before the first one
        bool afterBlank = false;

        for (int i = start; i < end; i++)
        {
            char c = doc[i];
            int width = c == '\t' ? (((column / TabWidth) + 1) * TabWidth) - column : 1;
            bool blank = c is ' ' or '\t' or '\r';

            if (!blank)
            {
                if (afterBlank && column > segStart)
                {
                    wordStart = column; // a candidate to break at, should this row overflow later
                }

                // Only a visible character can force a fold; blanks are allowed to hang past the edge.
                if (column > segStart && column + width > segStart + perRow)
                {
                    int next = wordStart > segStart ? wordStart : column;
                    FoldThroughBlanks(ref folds, ref segStart, next, perRow);
                    segStart = next;
                    (folds ??= []).Add(segStart);
                    wordStart = -1; // the new row has seen no boundary of its own yet
                }
            }

            afterBlank = blank;
            column += width;
        }

        // The same for blanks the line simply ends in: nothing followed them to force the fold above.
        FoldThroughBlanks(ref folds, ref segStart, column, perRow);

        if (folds is null)
        {
            return new LineWrap(OneRow, column);
        }

        var starts = new int[folds.Count + 1];
        folds.CopyTo(starts, 1); // starts[0] stays 0 — a line's first row begins at its first column
        return new LineWrap(starts, column);
    }

    /// <summary>
    /// Fold through a run of blanks that has hung more than a whole row past the edge, so that no row ever
    /// begins more than one row's width after the last. Letting a few invisible spaces spill past the right
    /// edge is what keeps a fold landing on the word after them; letting a hundred spill would put the
    /// caret, and every column behind it, somewhere wrapping has no horizontal scroll to reach.
    ///
    /// <para>Only blanks are ever crossed here: a visible character can never sit more than
    /// <paramref name="perRow"/> columns past its row's start, because it would have folded the row itself.</para>
    /// </summary>
    private static void FoldThroughBlanks(ref List<int>? folds, ref int segStart, int upTo, int perRow)
    {
        while (segStart + (2 * perRow) <= upTo)
        {
            segStart += perRow;
            (folds ??= []).Add(segStart);
        }
    }

    /// <summary>Visual rows a line occupies: one, unless wrapping folds it.</summary>
    internal int RowsInLine(int line) => !WordWrap ? 1 : WrapOf(line).Starts.Length;

    /// <summary>The expanded column range <c>[Start, End)</c> that one visual row of a line covers.</summary>
    internal (int Start, int End) SegmentColumns(int line, int segment)
    {
        if (!WordWrap)
        {
            return (0, LineColumns(line));
        }

        var wrap = WrapOf(line);
        int seg = Math.Clamp(segment, 0, wrap.Starts.Length - 1);
        return (wrap.Starts[seg], seg + 1 < wrap.Starts.Length ? wrap.Starts[seg + 1] : wrap.Columns);
    }

    /// <summary>Which of a line's visual rows an expanded column falls on. A fold point belongs to the row it starts.</summary>
    internal int SegmentAtColumn(int line, int column)
    {
        if (!WordWrap)
        {
            return 0;
        }

        var starts = WrapOf(line).Starts;
        int lo = 0, hi = starts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (starts[mid] <= column)
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

    /// <summary>The offset at the end of one visual row of <paramref name="line"/>.</summary>
    private int RowEndOffsetFor(int line, int segment) => !WordWrap
        ? Buffer.Document.GetLineEnd(line)
        : OffsetAtColumn(line, SegmentColumns(line, segment).End);
}
