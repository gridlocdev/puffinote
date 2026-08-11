using Puffinote.Text.Document;

namespace Typage.Control.Rendering;

/// <summary>What a <see cref="VisualRun"/> is: drawn text, a hidden (zero-width) marker, or a synthetic adornment.</summary>
public enum VisualRunKind
{
    /// <summary>Visible text (content, or a revealed marker): drawn, advances the x cursor.</summary>
    Text,

    /// <summary>A concealed marker: covers a source range but has <b>zero width</b> and isn't drawn.</summary>
    Concealed,

    /// <summary>A synthetic glyph/adornment not present in the source (bullet, checkbox, …): advances width, covers no columns.</summary>
    Decoration,
}

/// <summary>
/// One run of a laid-out line. <see cref="Col"/>/<see cref="Len"/> are columns of source content the
/// run covers (a <see cref="VisualRunKind.Decoration"/> covers none); <see cref="X"/>/<see cref="Width"/>
/// are its laid-out horizontal position and advance; <see cref="Row"/> is the wrapped visual row it sits
/// on (0 when the line doesn't wrap).
/// </summary>
public readonly record struct VisualRun(
    VisualRunKind Kind, int Col, int Len, double X, double Width, string Text, RunStyle Style,
    DecorationKind Decoration = DecorationKind.None, int Row = 0);

/// <summary>The kinds of synthetic adornment a decoration run draws (geometry lives in the surface/theme).</summary>
public enum DecorationKind
{
    None,
    Bullet,

    /// <summary>A task checkbox. <i>Which</i> of the three states it is riding is carried in the run's
    /// <see cref="VisualRun.Text"/> — the one box character (<c>' '</c>, <c>'x'</c>, <c>'/'</c>, …) — so the
    /// vocabulary can grow without a new decoration kind per state, and drawing/hit-testing match on one kind.</summary>
    Checkbox,
    QuoteBar,
    HorizontalRule,

    /// <summary>A computed ordered-list ordinal (e.g. "3. ") drawn in place of the literal source digits.</summary>
    OrderedNumber,

    /// <summary>The side padding of a background chip (an inline-code span's concealed backticks, a highlight's
    /// <c>==</c>): a spacer that draws nothing and reserves the room the chip is bled into.</summary>
    ChipPad,

    /// <summary>A nested list item's leading indentation: a spacer that draws nothing and advances one fixed step
    /// per nesting level, in place of the measured width of the item's source spaces.</summary>
    ListIndent,

    /// <summary>A picture drawn in place of a concealed <c>![alt](url)</c> that is alone on its line. The run's
    /// <see cref="VisualRun.Text"/> carries the image's <i>URL</i>, which the surface resolves against its own
    /// bitmap cache; its width is the drawn width and its row is as tall as the picture.</summary>
    Image,
}

/// <summary>
/// The laid-out visual model of a single document line and the <b>bidirectional offset↔visual map</b> it
/// implies (PRD §7.4). Built from pre-classified <see cref="Segment"/>s (the surface decides which markers
/// conceal) plus a width-measuring delegate, so it carries no Markdown knowledge and is testable without a
/// window. Concealed marker runs collapse to zero width — that collapse is what makes hidden markers vanish
/// while caret/click/selection still resolve to correct offsets through the map. When a positive
/// <c>wrapWidth</c> is given the line soft-wraps into multiple <see cref="RowCount"/> rows at word
/// boundaries, continuation rows hanging under the content (past any leading decoration gutter).
/// </summary>
public sealed class VisualLine
{
    private readonly string _text;
    private readonly Func<string, RunStyle, double> _measure;

    private VisualLine(string text, IReadOnlyList<VisualRun> runs, double width, int rowCount, Func<string, RunStyle, double> measure)
    {
        _text = text;
        Runs = runs;
        Width = width;
        RowCount = rowCount;
        _measure = measure;
    }

    /// <summary>The runs in left-to-right, top-to-bottom order; each tagged with its wrapped <see cref="VisualRun.Row"/>.</summary>
    public IReadOnlyList<VisualRun> Runs { get; }

    /// <summary>Width of the widest row.</summary>
    public double Width { get; }

    /// <summary>Number of wrapped visual rows (1 when the line fits or wrapping is off).</summary>
    public int RowCount { get; }

    /// <summary>The line's source content length in chars (columns), independent of concealment.</summary>
    public int RawLength => _text.Length;

    /// <summary>
    /// One pre-classified span of a line: source columns <c>[From, To)</c> in <paramref name="Style"/>,
    /// flagged <see cref="Concealed"/> when it's a marker the surface decided to hide. A
    /// <see cref="Decoration"/> other than <see cref="DecorationKind.None"/> makes this a synthetic
    /// leading adornment (no glyphs) of the given pixel <see cref="DecorationWidth"/>. <see cref="DecorationText"/>
    /// is what the adornment needs to know beyond its kind: the drawn digits of an
    /// <see cref="DecorationKind.OrderedNumber"/>, the box character of a <see cref="DecorationKind.Checkbox"/>.
    /// </summary>
    public readonly record struct Segment(
        int From, int To, RunStyle Style, bool Concealed,
        DecorationKind Decoration = DecorationKind.None, double DecorationWidth = 0, string DecorationText = "");

    /// <summary>
    /// Lay out <paramref name="segments"/> (in column order) over <paramref name="lineText"/>, measuring
    /// with <paramref name="measure"/>. A positive <paramref name="wrapWidth"/> soft-wraps the line at word
    /// boundaries into multiple rows; zero/negative lays it out on a single row.
    /// </summary>
    public static VisualLine Build(
        string lineText, IReadOnlyList<Segment> segments, Func<string, RunStyle, double> measure, double wrapWidth = 0)
    {
        // The leading decoration gutter (bullet/number/checkbox/quote width) that continuation rows hang under.
        var gutter = 0d;
        foreach (var s in segments)
        {
            if (s.Decoration == DecorationKind.ChipPad)
            {
                break; // a chip's inner padding is part of the content, not a line-prefix gutter
            }

            if (s.Decoration != DecorationKind.None)
            {
                gutter += s.DecorationWidth;
            }
            else if (!s.Concealed && s.To > s.From)
            {
                break; // first *visible* content span ends the leading run of decorations
            }

            // A concealed marker (e.g. the "- " of a task item, hidden ahead of its checkbox decoration)
            // is zero-width and must not end the gutter scan, or the following decoration's width is lost
            // and continuation rows hang at the far left instead of under the item's text.
        }

        var runs = new List<VisualRun>(segments.Count);
        var row = 0;
        var x = 0d;
        var rowWidth = 0d;
        var maxWidth = 0d;
        var wrap = wrapWidth > 0;

        void Place(VisualRunKind kind, int col, int len, double w, string text, RunStyle style, DecorationKind deco)
        {
            runs.Add(new VisualRun(kind, col, len, x, w, text, style, deco, row));
            x += w;
            rowWidth = x;
            if (rowWidth > maxWidth)
            {
                maxWidth = rowWidth;
            }
        }

        void Break()
        {
            maxWidth = Math.Max(maxWidth, rowWidth);
            row++;
            x = gutter;
            rowWidth = gutter;
        }

        foreach (var s in segments)
        {
            if (s.Decoration != DecorationKind.None)
            {
                // A decoration replaces a concealed marker: it owns the marker's columns [From,To) (atomic,
                // like a concealed run) and advances a gutter so the content hangs.
                Place(VisualRunKind.Decoration, s.From, s.To - s.From, s.DecorationWidth, s.DecorationText, s.Style, s.Decoration);
                continue;
            }

            if (s.To <= s.From)
            {
                continue;
            }

            if (s.Concealed)
            {
                Place(VisualRunKind.Concealed, s.From, s.To - s.From, 0, "", s.Style, DecorationKind.None);
                continue;
            }

            var text = lineText[s.From..s.To];
            if (!wrap)
            {
                Place(VisualRunKind.Text, s.From, s.To - s.From, measure(text, s.Style), text, s.Style, DecorationKind.None);
                continue;
            }

            // Word-wrap this text span: split into maximal runs of spaces / non-spaces, breaking before a
            // non-space piece that would overflow the row (trailing spaces are allowed to spill so every
            // source column keeps a position).
            var i = 0;
            while (i < text.Length)
            {
                var space = text[i] == ' ';
                var j = i;
                while (j < text.Length && (text[j] == ' ') == space)
                {
                    j++;
                }

                var piece = text[i..j];
                var w = measure(piece, s.Style);
                if (!space && x + w > wrapWidth && x > gutter)
                {
                    Break();
                }

                Place(VisualRunKind.Text, s.From + i, j - i, w, piece, s.Style, DecorationKind.None);
                i = j;
            }
        }

        maxWidth = Math.Max(maxWidth, rowWidth);
        return new VisualLine(lineText, runs, maxWidth, row + 1, measure);
    }

    /// <summary>
    /// The x of source column <paramref name="col"/> within the row it belongs to (see <see cref="RowOf"/>).
    /// At a soft-wrap boundary the column starting the next row wins, so a caret there sits at the left of
    /// the wrapped row (the convention every editor uses).
    /// </summary>
    public double ColumnToX(int col)
    {
        foreach (var r in Runs)
        {
            if (r.Len == 0)
            {
                continue;
            }

            if (col >= r.Col && col < r.Col + r.Len)
            {
                return r.Kind == VisualRunKind.Text ? r.X + _measure(r.Text[..(col - r.Col)], r.Style) : r.X;
            }
        }

        return Runs.Count > 0 ? Runs[^1].X + Runs[^1].Width : 0; // past the last column → line end
    }

    /// <summary>The wrapped row source column <paramref name="col"/> lays out on (next row wins at a soft-wrap boundary).</summary>
    public int RowOf(int col)
    {
        var lastRow = 0;
        foreach (var r in Runs)
        {
            if (r.Len == 0)
            {
                continue;
            }

            lastRow = r.Row;
            if (col >= r.Col && col < r.Col + r.Len)
            {
                return r.Row;
            }
        }

        return lastRow;
    }

    /// <summary>The x of column <paramref name="col"/> measured within visual <paramref name="row"/> only (row-local layout).</summary>
    private double XOnRow(int row, int col)
    {
        VisualRun? last = null;
        foreach (var r in Runs)
        {
            if (r.Row != row || r.Len == 0)
            {
                continue;
            }

            last = r;
            var end = r.Col + r.Len;
            if (col < r.Col)
            {
                return r.X;
            }

            if (col < end)
            {
                return r.Kind == VisualRunKind.Text ? r.X + _measure(r.Text[..(col - r.Col)], r.Style) : r.X;
            }

            if (col == end)
            {
                return r.X + r.Width;
            }
        }

        return last is { } l ? l.X + l.Width : 0;
    }

    /// <summary>The caret column nearest <paramref name="x"/> on visual <paramref name="row"/> (never strictly inside a concealed run).</summary>
    public int XToColumn(int row, double x)
    {
        // Restrict the scan to the columns that live on this row, measured in that row's local layout.
        var rowStart = int.MaxValue;
        var rowEnd = 0;
        foreach (var r in Runs)
        {
            if (r.Len == 0 || r.Row != row)
            {
                continue;
            }

            rowStart = Math.Min(rowStart, r.Col);
            rowEnd = Math.Max(rowEnd, r.Col + r.Len);
        }

        if (rowStart == int.MaxValue)
        {
            return 0; // an empty row (shouldn't happen) — fall back to the line start
        }

        var best = rowStart;
        var bestDist = double.MaxValue;
        var col = rowStart;
        while (true)
        {
            if (!IsInsideConcealed(col))
            {
                var d = Math.Abs(XOnRow(row, col) - x);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = col;
                }
            }

            if (col >= rowEnd)
            {
                break;
            }

            col = Grapheme.NextBoundary(_text, col);
        }

        return best;
    }

    /// <summary>The caret column nearest <paramref name="x"/> on the first row (single-row convenience).</summary>
    public int XToColumn(double x) => XToColumn(0, x);

    /// <summary>
    /// The horizontal spans, one per wrapped row, covering source columns <c>[from, to)</c> — used to draw
    /// a selection or search highlight that may break across rows.
    /// </summary>
    public IEnumerable<(int Row, double X1, double X2)> Spans(int from, int to)
    {
        if (to <= from)
        {
            yield break;
        }

        for (var row = 0; row < RowCount; row++)
        {
            var rowStart = int.MaxValue;
            var rowEnd = 0;
            foreach (var r in Runs)
            {
                if (r.Len == 0 || r.Row != row)
                {
                    continue;
                }

                rowStart = Math.Min(rowStart, r.Col);
                rowEnd = Math.Max(rowEnd, r.Col + r.Len);
            }

            if (rowStart == int.MaxValue)
            {
                continue;
            }

            var a = Math.Max(from, rowStart);
            var b = Math.Min(to, rowEnd);
            if (b > a)
            {
                yield return (row, XOnRow(row, a), XOnRow(row, b));
            }
        }
    }

    /// <summary>True when <paramref name="col"/> is strictly inside an atomic (concealed or decoration) run — not a valid caret stop.</summary>
    public bool IsInsideConcealed(int col)
    {
        foreach (var r in Runs)
        {
            if (r.Kind != VisualRunKind.Text && col > r.Col && col < r.Col + r.Len)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the <b>character</b> at column <paramref name="col"/> is hidden — it belongs to a concealed
    /// marker run or a decoration's source span. Unlike <see cref="IsInsideConcealed"/> (which excludes run
    /// boundaries so the caret can stop there), this covers the whole hidden span, so edits can step over
    /// hidden marker punctuation to reach the next visible grapheme.
    /// </summary>
    public bool IsColumnHidden(int col)
    {
        foreach (var r in Runs)
        {
            if (r.Kind != VisualRunKind.Text && col >= r.Col && col < r.Col + r.Len)
            {
                return true;
            }
        }

        return false;
    }
}
