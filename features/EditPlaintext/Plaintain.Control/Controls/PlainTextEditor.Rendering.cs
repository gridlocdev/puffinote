using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Puffinote.Components.Theming;
using Puffinote.Text.Document;

namespace Plaintain.Control.Controls;

public sealed partial class PlainTextEditor
{
    /// <summary>The colours the surface paints with, resolved for the active theme variant.</summary>
    private sealed record Palette(IBrush Canvas, IBrush Text, IBrush GutterFill, IBrush GutterText, IBrush CurrentRow, IBrush Selection, IBrush Caret, IBrush Match);

    private static Palette PaletteFor(ThemeVariant variant)
    {
        var tokens = Tokens.For(variant);
        bool dark = variant == ThemeVariant.Dark;
        return new Palette(
            Canvas: Brush(dark ? 0xFF1E1E1E : 0xFFFFFFFF),
            Text: Brush(dark ? 0xFFE6E6E6 : 0xFF1A1A1A),
            GutterFill: Brush(dark ? 0xFF232323 : 0xFFF6F6F6),
            GutterText: tokens.MutedText,
            // The caret's row is a locator, not a highlight. It runs the full width of the viewport on
            // every row the caret rests on, so an alpha that reads as "tinted" on a short line reads as a
            // bar across the window on a log — hence deliberately fainter than Typage's, which is bounded
            // by the content column. Just enough to find the caret with, not enough to look at.
            CurrentRow: Brush(dark ? 0x14FFFFFFu : 0x0A000000u),
            Selection: tokens.AccentSubtle,
            Caret: tokens.Accent,
            Match: Brush(dark ? 0x553FB084u : 0x5515734Fu));
    }

    private static IBrush Brush(uint argb) => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromUInt32(argb));

    /// <summary>Matches the find bar has highlighted, in document offsets.</summary>
    private IReadOnlyList<Puffinote.Text.Document.SearchMatch> _matches = [];

    /// <summary>The match the find bar has stepped to, drawn brighter than the rest.</summary>
    private int _currentMatch = -1;

    internal void SetMatches(IReadOnlyList<Puffinote.Text.Document.SearchMatch> matches, int current)
    {
        _matches = matches;
        _currentMatch = current;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var palette = PaletteFor(ActualThemeVariant);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(palette.Canvas, bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var doc = Buffer.Document;
        int totalRows = Math.Max(1, TotalRows);
        int firstRow = Math.Max(0, (int)(_offset.Y / _rowHeight));
        int lastRow = Math.Min(totalRows - 1, (int)((_offset.Y + bounds.Height) / _rowHeight));
        var (caretRow, caretColumn) = CaretRowColumn;

        double gutter = GutterWidth;
        if (gutter > 0)
        {
            context.FillRectangle(palette.GutterFill, new Rect(0, 0, gutter, bounds.Height));
        }

        // Widen the horizontal extent from what is actually on screen. Measuring every line of a 50 MB
        // file to find the longest one would undo the whole point of never touching a row you can't see;
        // the bar grows as you scroll into longer lines, which is what a log reader expects anyway.
        int widest = _widestColumns;

        using (context.PushClip(new Rect(gutter, 0, Math.Max(0, bounds.Width - gutter), bounds.Height)))
        {
            for (int row = firstRow; row <= lastRow; row++)
            {
                double y = (row * _rowHeight) - _offset.Y;
                var (line, segment) = LineAtRow(row);
                if (line >= doc.LineCount)
                {
                    break;
                }

                string lineText = doc.GetLineText(line).TrimEnd('\r');
                string rowText = SegmentOf(line, lineText, segment, out int segmentStartColumn);
                widest = Math.Max(widest, ExpandedLength(lineText));

                if (row == caretRow && HighlightCurrentLine)
                {
                    context.FillRectangle(palette.CurrentRow, new Rect(gutter, y, Math.Max(0, bounds.Width - gutter), _rowHeight));
                }

                DrawRowSelection(context, palette, row, line, segment, segmentStartColumn, y, bounds);
                DrawRowMatches(context, palette, line, segment, segmentStartColumn, y);

                var formatted = new FormattedText(Expand(rowText, segmentStartColumn), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, _typeface, ScaledFontSize, palette.Text);
                context.DrawText(formatted, new Point(TextLeft - _offset.X, y + ((_rowHeight - formatted.Height) / 2)));
            }
        }

        _widestColumns = widest;

        // The gutter is drawn last so a long line scrolled left never bleeds under the numbers.
        if (gutter > 0)
        {
            DrawGutter(context, palette, firstRow, lastRow, bounds, caretRow);
        }

        DrawCaret(context, palette, caretRow, caretColumn, bounds);
    }

    /// <summary>The slice of <paramref name="lineText"/> that row <paramref name="segment"/> shows.</summary>
    private string SegmentOf(int line, string lineText, int segment, out int startColumn)
    {
        startColumn = 0;
        if (!WordWrap)
        {
            return lineText;
        }

        (startColumn, int endColumn) = SegmentColumns(line, segment);

        // Walk by expanded column so a tab is counted as the width it draws, not as one character.
        int column = 0, from = -1;
        for (int i = 0; i <= lineText.Length; i++)
        {
            if (from < 0 && column >= startColumn)
            {
                from = i;
            }

            if (from >= 0 && column >= endColumn)
            {
                return lineText[from..i];
            }

            if (i < lineText.Length)
            {
                column = lineText[i] == '\t' ? ((column / TabWidth) + 1) * TabWidth : column + 1;
            }
        }

        return from >= 0 ? lineText[from..] : string.Empty;
    }

    /// <summary>
    /// Expand tabs to spaces for drawing. FormattedText has no tab-stop model, so a literal tab would
    /// render as a single blank and every column below it would be a lie — the one place a plaintext
    /// editor's alignment is load-bearing (a log, a table pasted from anywhere).
    /// </summary>
    private static string Expand(string text, int startColumn)
    {
        if (!text.Contains('\t', StringComparison.Ordinal))
        {
            return text;
        }

        var sb = new System.Text.StringBuilder(text.Length + TabWidth);
        int column = startColumn;
        foreach (char c in text)
        {
            if (c == '\t')
            {
                int next = ((column / TabWidth) + 1) * TabWidth;
                sb.Append(' ', next - column);
                column = next;
            }
            else
            {
                sb.Append(c);
                column++;
            }
        }

        return sb.ToString();
    }

    private static int ExpandedLength(string text)
    {
        int column = 0;
        foreach (char c in text)
        {
            column = c == '\t' ? ((column / TabWidth) + 1) * TabWidth : column + 1;
        }

        return column;
    }

    /// <summary>
    /// Fill this row's share of every selection. Usually there is one; after ⌘D there are several, and
    /// they are drawn the same — they are the same kind of thing, and marking the primary differently
    /// would suggest the others were less real when the next keystroke reaches all of them equally.
    /// </summary>
    private void DrawRowSelection(DrawingContext context, Palette palette, int row, int line, int segment, int startColumn, double y, Rect bounds)
    {
        DrawOneSelection(context, palette, Buffer.Selection, row, line, segment, startColumn, y, bounds);

        // The extras are read straight from the list rather than through AllSelections(), which sorts a
        // fresh List — this runs once per visible row, on every frame, and a per-row allocation in the
        // paint path is the kind of thing that only shows up as a stutter while scrolling.
        foreach (var selection in _extraSelections)
        {
            DrawOneSelection(context, palette, selection, row, line, segment, startColumn, y, bounds);
        }
    }

    private void DrawOneSelection(DrawingContext context, Palette palette, Selection sel, int row, int line, int segment, int startColumn, double y, Rect bounds)
    {
        if (sel.IsEmpty)
        {
            return;
        }

        int rowStart = OffsetAt(row, 0);
        int rowEnd = segment >= 0 ? RowEndOffsetFor(line, segment) : rowStart;
        int from = Math.Max(sel.Start, rowStart);
        int to = Math.Min(sel.End, rowEnd);
        if (to < from)
        {
            return;
        }

        double x1 = TextLeft - _offset.X + ((ColumnOf(line, from) - startColumn) * _charWidth);
        double x2 = TextLeft - _offset.X + ((ColumnOf(line, to) - startColumn) * _charWidth);

        // A selection that runs past this row's end swallows the newline; showing a sliver of trailing
        // width is how every editor says "the line break is selected too".
        if (sel.End > rowEnd && rowEnd < Buffer.Document.Length)
        {
            x2 += _charWidth / 2;
        }

        context.FillRectangle(palette.Selection, new Rect(x1, y, Math.Max(1, x2 - x1), _rowHeight));
        _ = bounds;
    }

    private void DrawRowMatches(DrawingContext context, Palette palette, int line, int segment, int startColumn, double y)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        int rowStart = OffsetAtColumn(line, startColumn);
        int rowEnd = RowEndOffsetFor(line, segment);
        for (int i = 0; i < _matches.Count; i++)
        {
            var match = _matches[i];
            if (match.End < rowStart || match.Start > rowEnd)
            {
                continue;
            }

            int from = Math.Max(match.Start, rowStart);
            int to = Math.Min(match.End, rowEnd);
            double x1 = TextLeft - _offset.X + ((ColumnOf(line, from) - startColumn) * _charWidth);
            double x2 = TextLeft - _offset.X + ((ColumnOf(line, to) - startColumn) * _charWidth);
            var brush = i == _currentMatch ? palette.Caret : palette.Match;
            context.FillRectangle(brush, new Rect(x1, y, Math.Max(1, x2 - x1), _rowHeight));
        }
    }

    private void DrawGutter(DrawingContext context, Palette palette, int firstRow, int lastRow, Rect bounds, int caretRow)
    {
        double gutter = GutterWidth;
        context.FillRectangle(palette.GutterFill, new Rect(0, 0, gutter, bounds.Height));

        // A document whose encoding was guessed can't be written back; dimming the gutter says the state
        // continuously, where a dialog said it once and then went away.
        double opacity = IsReadOnlyEncoding ? 0.45 : 1.0;

        for (int row = firstRow; row <= lastRow; row++)
        {
            var (line, segment) = LineAtRow(row);
            if (segment != 0 || line >= Buffer.Document.LineCount)
            {
                continue; // a wrapped continuation row is the same line — number it once
            }

            var text = new FormattedText((line + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, ScaledFontSize,
                row == caretRow ? palette.Text : palette.GutterText);
            double y = (row * _rowHeight) - _offset.Y;
            using (context.PushOpacity(opacity))
            {
                context.DrawText(text, new Point(gutter - GutterGap - PadX - text.Width, y + ((_rowHeight - text.Height) / 2)));
            }
        }
    }

    private void DrawCaret(DrawingContext context, Palette palette, int row, int column, Rect bounds)
    {
        // Unfocused: no caret at all. Focused: only on the lit half of the blink (CaretBlink).
        if (!IsFocused || !_caretBlink.IsCaretVisible)
        {
            return;
        }

        // Every selection has a caret, and after ⌘D they all blink together — one rhythm, because they
        // are one cursor in several places rather than several cursors.
        foreach (var extra in _extraSelections)
        {
            var (extraRow, extraColumn) = RowColumnOf(extra.Caret);
            DrawOneCaret(context, palette, extraRow, extraColumn, bounds);
        }

        DrawOneCaret(context, palette, row, column, bounds);
    }

    private void DrawOneCaret(DrawingContext context, Palette palette, int row, int column, Rect bounds)
    {
        double x = TextLeft - _offset.X + (column * _charWidth);
        double y = (row * _rowHeight) - _offset.Y;
        if (x < GutterWidth || x > bounds.Width || y + _rowHeight < 0 || y > bounds.Height)
        {
            return;
        }

        context.FillRectangle(palette.Caret, new Rect(x, y, 2, _rowHeight));
    }

    /// <summary>
    /// Test seam: the width the renderer will actually lay <paramref name="text"/> out at — measured
    /// through the same typeface and size <see cref="Render"/> draws with, so a test can hold the drawn
    /// text against the column arithmetic the caret is placed by.
    /// </summary>
    internal double MeasureRowForTest(string text) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, ScaledFontSize,
            Brushes.Black).WidthIncludingTrailingWhitespace;

    /// <summary>
    /// Test seam: step the caret's blink one phase, as its timer would. A headless test has no wall clock
    /// to wait on, so this is how it observes the caret going dark and coming back.
    /// </summary>
    internal void AdvanceCaretBlinkForTest() => _caretBlink.Advance();

    /// <summary>
    /// Test seam: the tint the caret's row is filled with, for <paramref name="variant"/>. Exposed so the
    /// "muted, not distracting" intent above is pinned by a test rather than left to whoever next edits
    /// the palette literal.
    /// </summary>
    internal static Color CurrentRowTintForTest(ThemeVariant variant) =>
        ((ISolidColorBrush)PaletteFor(variant).CurrentRow).Color;

    /// <summary>Test seam: whether the blink is currently on its lit phase.</summary>
    internal bool IsCaretLitForTest => _caretBlink.IsCaretVisible;

    /// <summary>Test seam: whether the blink's timer is running — the caret is blinking, not just lit.</summary>
    internal bool IsCaretBlinkingForTest => _caretBlink.IsBlinking;

    /// <summary>Test seam: the caret's pixel rectangle, so a headless test can check where it is drawn.</summary>
    internal Rect CaretRectForTest()
    {
        var (row, column) = CaretRowColumn;
        return new Rect(TextLeft - _offset.X + (column * _charWidth), (row * _rowHeight) - _offset.Y, 2, _rowHeight);
    }
}
