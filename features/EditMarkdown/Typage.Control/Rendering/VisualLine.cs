using Typage.Engine.Document;

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
/// are its laid-out horizontal position and advance.
/// </summary>
public readonly record struct VisualRun(
    VisualRunKind Kind, int Col, int Len, double X, double Width, string Text, RunStyle Style, DecorationKind Decoration = DecorationKind.None);

/// <summary>The kinds of synthetic adornment a decoration run draws (geometry lives in the surface/theme).</summary>
public enum DecorationKind
{
    None,
    Bullet,
    CheckboxUnchecked,
    CheckboxChecked,
    QuoteBar,
    HorizontalRule,

    /// <summary>A computed ordered-list ordinal (e.g. "3. ") drawn in place of the literal source digits.</summary>
    OrderedNumber,
}

/// <summary>
/// The laid-out visual model of a single line and the <b>bidirectional offset↔visual map</b> it
/// implies (PRD §7.4). Built from pre-classified <see cref="Segment"/>s (the surface decides which
/// markers conceal) plus a width-measuring delegate, so it carries no Markdown knowledge and is
/// testable without a window. Concealed marker runs collapse to zero width — that collapse is the
/// whole point: it is what makes hidden markers vanish while caret/click/selection still resolve to
/// correct document offsets through <see cref="ColumnToX"/>/<see cref="XToColumn"/>.
/// </summary>
public sealed class VisualLine
{
    private readonly string _text;
    private readonly Func<string, RunStyle, double> _measure;

    private VisualLine(string text, IReadOnlyList<VisualRun> runs, double width, Func<string, RunStyle, double> measure)
    {
        _text = text;
        Runs = runs;
        Width = width;
        _measure = measure;
    }

    /// <summary>The runs in left-to-right order; text/concealed runs tile the content columns, decorations interleave.</summary>
    public IReadOnlyList<VisualRun> Runs { get; }

    /// <summary>Total laid-out width (advance) of the line, including decorations.</summary>
    public double Width { get; }

    /// <summary>The line's source content length in chars (columns), independent of concealment.</summary>
    public int RawLength => _text.Length;

    /// <summary>
    /// One pre-classified span of a line: source columns <c>[From, To)</c> in <paramref name="Style"/>,
    /// flagged <see cref="Concealed"/> when it's a marker the surface decided to hide. A
    /// <see cref="Decoration"/> other than <see cref="DecorationKind.None"/> makes this a synthetic
    /// leading adornment (From==To, no columns) of the given pixel <see cref="DecorationWidth"/>.
    /// </summary>
    public readonly record struct Segment(
        int From, int To, RunStyle Style, bool Concealed,
        DecorationKind Decoration = DecorationKind.None, double DecorationWidth = 0, string DecorationText = "");

    /// <summary>Lay out <paramref name="segments"/> (in column order) over <paramref name="lineText"/>, measuring with <paramref name="measure"/>.</summary>
    public static VisualLine Build(string lineText, IReadOnlyList<Segment> segments, Func<string, RunStyle, double> measure)
    {
        var runs = new List<VisualRun>(segments.Count);
        var x = 0d;
        foreach (var s in segments)
        {
            if (s.Decoration != DecorationKind.None)
            {
                // A decoration replaces a concealed marker: it owns that marker's source columns
                // [From, To) (so the caret treats them atomically, like a concealed run) but advances a
                // gutter of DecorationWidth, giving the content a hanging indent.
                runs.Add(new VisualRun(VisualRunKind.Decoration, s.From, s.To - s.From, x, s.DecorationWidth, s.DecorationText, s.Style, s.Decoration));
                x += s.DecorationWidth;
                continue;
            }

            if (s.To <= s.From)
            {
                continue;
            }

            if (s.Concealed)
            {
                runs.Add(new VisualRun(VisualRunKind.Concealed, s.From, s.To - s.From, x, 0, "", s.Style));
                continue;
            }

            var text = lineText.Substring(s.From, s.To - s.From);
            var w = measure(text, s.Style);
            runs.Add(new VisualRun(VisualRunKind.Text, s.From, s.To - s.From, x, w, text, s.Style));
            x += w;
        }

        return new VisualLine(lineText, runs, x, measure);
    }

    /// <summary>
    /// The x of source column <paramref name="col"/>. A concealed/decoration run is atomic: an interior
    /// column maps to its left edge; the run's end-boundary maps past its width (0 for a hidden marker,
    /// the gutter for a decoration), so content keeps its hanging indent.
    /// </summary>
    public double ColumnToX(int col)
    {
        foreach (var r in Runs)
        {
            if (r.Len == 0)
            {
                continue; // a column-less overlay; its width (if any) is already baked into later runs' X
            }

            var end = r.Col + r.Len;
            if (col <= r.Col)
            {
                return r.X;
            }

            if (col < end)
            {
                return r.Kind == VisualRunKind.Text ? r.X + _measure(r.Text[..(col - r.Col)], r.Style) : r.X;
            }

            if (col == end)
            {
                return r.X + r.Width; // just past the run: text full width, decoration gutter, or 0
            }
        }

        return Width;
    }

    /// <summary>The caret column nearest x (a valid stop — never strictly inside a concealed run).</summary>
    public int XToColumn(double x)
    {
        var best = 0;
        var bestDist = double.MaxValue;
        var col = 0;
        while (true)
        {
            if (!IsInsideConcealed(col))
            {
                var d = Math.Abs(ColumnToX(col) - x);
                // <= so that when a collapsed marker makes several columns share one x (e.g. column 0
                // and the content start both sit at x=0 behind a hidden prefix), the click resolves to
                // the rightmost — i.e. lands on the visible content, not before the hidden marker.
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = col;
                }
            }

            if (col >= _text.Length)
            {
                break;
            }

            col = Grapheme.NextBoundary(_text, col);
        }

        return best;
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
    /// True when the <b>character</b> at column <paramref name="col"/> is hidden — i.e. it belongs to a
    /// concealed marker run or a decoration's source span. Unlike <see cref="IsInsideConcealed"/> (which
    /// excludes run boundaries so the caret can stop there), this covers the whole hidden span, so edits
    /// can step over hidden marker punctuation to reach the next visible grapheme.
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
