using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using SAlign = Cellestra.Core.Styling.HorizontalAlignment;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

public sealed partial class SpreadsheetGrid
{
    // ---- Rendering --------------------------------------------------------

    private void DrawTo(DrawingContext context)
    {
        BuildVisible(Bounds.Size);
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        // Re-resolve here too (not just on theme change) so all partials draw with one consistent palette.
        _palette = GridPalette.For(ActualThemeVariant);
        var gridPen = _palette.GridLine;

        context.FillRectangle(_palette.Canvas, new Rect(Bounds.Size));

        // Cell content is clipped to the area below/right of the headers. Smooth scrolling draws the
        // first row/column partially off the top/left fold (by its sub-cell offset), and this clip keeps
        // that overhang from painting into the header gutters.
        using (context.PushClip(CellViewport()))
        {
            DrawTableBanding(context, sheet);
            DrawSelection(context, _palette.SelectionFill);
            DrawPointSelectionFill(context); // show the formula point-mode target with normal chrome
            DrawCells(context, sheet, gridPen);
            DrawMerges(context, sheet, gridPen);
            DrawCellBorders(context, sheet);
            DrawTableChrome(context, sheet);
        }

        DrawFreezeDividers(context);
        DrawHeaders(context, _palette.HeaderFill, _palette.HeaderText, gridPen);

        // Selection/edit overlays draw on top of the headers, so re-apply the same clip to keep the
        // active-cell outline, marquee, and fill handle of a partially-scrolled cell out of the gutters.
        using (context.PushClip(CellViewport()))
        {
            DrawActiveOutline(context, _palette.ActiveOutline);
            DrawPointSelectionOutline(context);
            DrawReferenceHighlights(context);
            DrawMarquee(context);
            DrawFillPreview(context);
            DrawFillHandle(context);
            DrawPaintPreview(context);
        }
    }

    /// <summary>The rectangle holding cell content — everything below the column header and right of the
    /// row header, out to the grid edge. Cell drawing is clipped to this so partial scroll rows/columns
    /// never bleed into the headers.</summary>
    private Rect CellViewport() => new(
        HeaderWidth,
        HeaderHeight,
        Math.Max(0, Bounds.Width - HeaderWidth),
        Math.Max(0, Bounds.Height - HeaderHeight));

    private void DrawCells(DrawingContext context, Worksheet sheet, IPen gridPen)
    {
        // Pass 1: backgrounds and gridlines for every visible cell.
        foreach (var (row, y, h) in _visibleRows)
        {
            foreach (var (col, x, w) in _visibleColumns)
            {
                if (sheet.FindMerge(row, col) is not null)
                {
                    continue; // merged regions are painted once, in DrawMerges
                }

                var rect = new Rect(x, y, w, h);
                var cell = sheet.Find(row, col);
                if (cell is not null)
                {
                    var style = ResolveStyle(row, col);
                    if (style.Fill.HasBackground)
                    {
                        context.FillRectangle(new SolidColorBrush(ToColor(style.Fill.Background!.Value)), rect);
                    }
                }

                context.DrawRectangle(null, gridPen, rect);
            }
        }

        // Pass 2: text, after all gridlines — so text spilling into empty neighbors isn't clipped.
        foreach (var (row, y, h) in _visibleRows)
        {
            foreach (var (col, x, w) in _visibleColumns)
            {
                if (sheet.FindMerge(row, col) is not null)
                {
                    continue;
                }

                var cell = sheet.Find(row, col);
                // A hyperlinked cell draws even when empty (its target stands in as the link text).
                if (cell is not null && (!cell.Value.IsEmpty || cell.Hyperlink is not null))
                {
                    DrawCellContent(context, sheet, cell, new Rect(x, y, w, h), row, col, allowSpill: true);
                }
            }
        }
    }

    private void DrawMerges(DrawingContext context, Worksheet sheet, IPen gridPen)
    {
        foreach (var region in sheet.MergedRegions)
        {
            if (!TryGetRegionRect(region.Range.Top, region.Range.Left, region.Range.Bottom, region.Range.Right, out var rect))
            {
                continue;
            }

            // Paint the anchor cell's fill across the whole merge (mirroring DrawCells, including any
            // live style-preview); fall back to the blank canvas when it has no background (task 7).
            var anchor = sheet.Find(region.Range.Top, region.Range.Left);
            if (anchor is not null && ResolveStyle(region.Range.Top, region.Range.Left).Fill is { HasBackground: true } fill)
            {
                context.FillRectangle(new SolidColorBrush(ToColor(fill.Background!.Value)), rect);
            }
            else
            {
                context.FillRectangle(_palette.Canvas, rect);
            }

            context.DrawRectangle(null, gridPen, rect);

            if (anchor is not null && !anchor.Value.IsEmpty)
            {
                DrawCellContent(context, sheet, anchor, rect, region.Range.Top, region.Range.Left, allowSpill: false);
            }
        }
    }

    private void DrawFreezeDividers(DrawingContext context)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        var pen = _palette.FreezeDivider;
        if (sheet.FrozenColumns > 0 && _frozenRight > HeaderWidth)
        {
            context.DrawLine(pen, new Point(_frozenRight, 0), new Point(_frozenRight, Bounds.Height));
        }

        if (sheet.FrozenRows > 0 && _frozenBottom > HeaderHeight)
        {
            context.DrawLine(pen, new Point(0, _frozenBottom), new Point(Bounds.Width, _frozenBottom));
        }
    }

    private bool TryGetRegionRect(int top, int left, int bottom, int right, out Rect rect)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var (col, x, w) in _visibleColumns)
        {
            if (col >= left && col <= right)
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x + w);
            }
        }

        foreach (var (row, y, h) in _visibleRows)
        {
            if (row >= top && row <= bottom)
            {
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y + h);
            }
        }

        if (minX > maxX || minY > maxY)
        {
            rect = default;
            return false;
        }

        rect = new Rect(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    private void DrawCellContent(DrawingContext context, Worksheet sheet, Cell cell, Rect rect, int row, int column, bool allowSpill)
    {
        var style = ResolveStyle(row, column);
        bool isLink = cell.Hyperlink is not null;
        string text = NumberFormatter.Format(cell.Value, style.NumberFormat, Culture);
        // An empty hyperlinked cell shows its target as the visible (clickable) link text.
        if (text.Length == 0 && isLink)
        {
            text = cell.Hyperlink!.Target;
        }

        if (text.Length == 0)
        {
            return;
        }

        var typeface = new Typeface(
            style.Font.Family,
            style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);
        double fontSize = style.Font.SizePoints * 96.0 / 72.0 * Zoom;
        // A hyperlink draws in the themed link colour and is always underlined, regardless of the
        // cell's own font colour/decorations, so links read as links in both light and dark mode.
        // Both colours are resolved against what is painted behind this cell — its own fill, the table
        // band under it, or the canvas — so neither can end up drawn in the surface's own colour.
        var background = CellBackground(style, row, column);
        var brush = isLink ? HyperlinkBrushFor(background) : TextBrushFor(style, cell.Value, background);

        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, fontSize, brush);
        if (LinkDecorations(style.Font, isLink) is { } decorations)
        {
            formatted.SetTextDecorations(decorations);
        }

        double cellAvailable = rect.Width - 2 * CellPadX;
        bool numeric = cell.Value.IsNumber || cell.Value.IsBoolean || cell.Value.IsError;

        // A number (or date) too wide for its column shows ##### rather than being trimmed — widen
        // the column to see it. Errors keep their token.
        if (numeric && !cell.Value.IsError && formatted.Width > cellAvailable)
        {
            DrawHashes(context, rect, typeface, fontSize, brush, cellAvailable);
            return;
        }

        var horizontal = style.Alignment.Horizontal switch
        {
            SAlign.General => numeric ? SAlign.Right : SAlign.Left,
            var h => h,
        };

        // Wrap Text: the text flows onto multiple lines within the column width (never spilling into
        // neighbours, never trimmed); the row grows to fit via EffectiveRowHeight (task 6). Numbers
        // ignore wrap (they ##### instead, handled above).
        bool wrap = style.Alignment.WrapText && !numeric;
        bool clipped = false;
        double drawableWidth = cellAvailable; // text-drawing width incl. any spill room, for the marker
        if (wrap)
        {
            formatted.MaxTextWidth = Math.Max(1, cellAvailable);
            formatted.Trimming = TextTrimming.None;
        }
        else
        {
            // Left-aligned text overflows into empty neighbours and only clips when one is occupied.
            double available = cellAvailable;
            if (allowSpill && !numeric && horizontal == SAlign.Left && formatted.Width > cellAvailable)
            {
                available += SpillRoom(sheet, row, column);
            }

            drawableWidth = available;

            // When text still doesn't fit, Calc clips it hard (no ellipsis) and flags the hidden
            // overflow with a small red double-chevron at the clipped edge. Reserve room for it so the
            // glyph never sits on top of the trimmed text.
            clipped = OverflowChevronSide(formatted.Width, available, horizontal == SAlign.Right) is not null;
            if (clipped)
            {
                formatted.MaxTextWidth = Math.Max(1, available - OverflowMarkerWidth(typeface, fontSize));
                formatted.Trimming = TextTrimming.None;
            }
            else
            {
                formatted.MaxTextWidth = Math.Max(1, available);
                formatted.Trimming = TextTrimming.CharacterEllipsis;
            }
        }

        double tx = horizontal switch
        {
            SAlign.Right => rect.Right - CellPadX - formatted.Width,
            SAlign.Center => rect.X + (rect.Width - formatted.Width) / 2,
            _ => rect.X + CellPadX,
        };
        double ty = style.Alignment.Vertical switch
        {
            Cellestra.Core.Styling.VerticalAlignment.Top => rect.Y + CellPadY,
            Cellestra.Core.Styling.VerticalAlignment.Middle => rect.Y + (rect.Height - formatted.Height) / 2,
            _ => rect.Bottom - formatted.Height - CellPadY, // Bottom (the default)
        };

        // Content overflows only rightwards, never downwards: clip the text to its own row so a font
        // taller than the row (or a font whose real line height exceeds the row-growth estimate) is
        // truncated at the cell's bottom edge instead of bleeding into the cell below. The clip spans
        // the full drawable width (incl. any spill room) so rightward overflow into empty neighbours is
        // preserved.
        var clip = new Rect(rect.X, rect.Y, 2 * CellPadX + drawableWidth, rect.Height);
        using (context.PushClip(clip))
        {
            context.DrawText(formatted, new Point(tx, Math.Max(rect.Y, ty)));

            if (clipped)
            {
                // Right-aligned text is cut off on its left; everything else on its right. The chevron
                // sits at the actual clip edge — past any neighbours the text spilled into.
                DrawOverflowMarker(context, rect, drawableWidth, typeface, fontSize, atRight: horizontal != SAlign.Right);
            }
        }
    }

    /// <summary>
    /// The single rule for the clipped-text chevron: returns which side it sits on (true = right) when
    /// the measured text is wider than the space it can draw in, or null when the text fits. The 0.5px
    /// tolerance keeps sub-pixel rounding from flagging text that visually fits exactly.
    /// </summary>
    internal static bool? OverflowChevronSide(double textWidth, double availableWidth, bool isRightAligned) =>
        textWidth > availableWidth + 0.5 ? !isRightAligned : null;

    /// <summary>Width reserved for the clipped-overflow chevron, including a small gap from the text.</summary>
    private static double OverflowMarkerWidth(Typeface typeface, double fontSize) =>
        new FormattedText("»", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black).Width + 2;

    /// <summary>Draws the red double-chevron that flags text clipped by its cell box (Calc-style).</summary>
    private void DrawOverflowMarker(DrawingContext context, Rect rect, double drawableWidth, Typeface typeface, double fontSize, bool atRight)
    {
        var glyph = new FormattedText(atRight ? "»" : "«", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, _palette.OverflowMarker);
        double mx = atRight ? rect.X + CellPadX + drawableWidth - glyph.Width : rect.X + CellPadX;
        double my = rect.Y + (rect.Height - glyph.Height) / 2;
        context.DrawText(glyph, new Point(mx, Math.Max(rect.Y, my)));
    }

    /// <summary>Extra width available to the right of a cell while the next columns are empty.</summary>
    private double SpillRoom(Worksheet sheet, int row, int column)
    {
        double extra = 0;
        for (int i = 0; i < _visibleColumns.Count; i++)
        {
            if (_visibleColumns[i].Index != column)
            {
                continue;
            }

            for (int j = i + 1; j < _visibleColumns.Count; j++)
            {
                int next = _visibleColumns[j].Index;
                var neighbour = sheet.Find(row, next);
                if ((neighbour is not null && !neighbour.Value.IsEmpty) || sheet.FindMerge(row, next) is not null)
                {
                    break; // an occupied (or merged) neighbour clips the spill
                }

                extra += _visibleColumns[j].Size;
            }

            break;
        }

        return extra;
    }

    private static void DrawHashes(DrawingContext context, Rect rect, Typeface typeface, double fontSize, IBrush brush, double available)
    {
        var one = new FormattedText("#", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);
        int count = Math.Max(1, (int)(available / Math.Max(1, one.Width)));
        var hashes = new FormattedText(new string('#', count), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);
        double tx = rect.Right - CellPadX - hashes.Width; // numbers right-align
        double ty = rect.Bottom - hashes.Height - CellPadY;
        context.DrawText(hashes, new Point(tx, Math.Max(rect.Y, ty)));
    }

    /// <summary>
    /// Test seam: the color a cell's text would be drawn in, for the current theme, on whatever the
    /// cell's own style puts behind it (its fill, else the bare canvas).
    /// </summary>
    internal Color ResolvedTextColor(CellStyle style, CellValue value) =>
        ((ISolidColorBrush)TextBrushFor(style, value, StyleBackground(style))).Color;

    /// <summary>The surface a cell's own style puts behind its text: its fill, else the themed canvas.</summary>
    private ArgbColor StyleBackground(CellStyle style) =>
        style.Fill.HasBackground ? style.Fill.Background!.Value : FromBrush(_palette.Canvas);

    /// <summary>
    /// The brush a cell's text is drawn with, given what is actually painted behind it.
    /// <para>
    /// The default/"automatic" font color — transparent, or the default black — is the app's choice,
    /// not the document's, so the app owns keeping it readable: it takes the theme's body ink, then
    /// gives way to whichever pole the surface isn't when that ink would vanish into the surface. That
    /// last step is what stops a light-filled cell (a table header, or any fill the document chose)
    /// from drawing pale-on-pale in dark mode. An explicitly chosen non-black color is document data
    /// and is drawn as-is — overriding it would misreport what the document says.
    /// </para>
    /// </summary>
    private IBrush TextBrushFor(CellStyle style, CellValue value, ArgbColor background)
    {
        if (value.IsError)
        {
            return Legible(Brushes.Crimson, background);
        }

        bool automatic = style.Font.Color.IsTransparent || style.Font.Color == ArgbColor.Black;
        return automatic
            ? Legible(_palette.DefaultText, background)
            : new SolidColorBrush(ToColor(style.Font.Color));
    }

    /// <summary>
    /// The blue a hyperlink draws in on <paramref name="background"/>. The link colour is the grid's
    /// own chrome, so it follows the same rule as automatic ink — except that a link has somewhere
    /// better to fall than plain body ink: the opposite theme's blue, which is picked when this
    /// theme's can't be read on the cell's fill. Only if neither blue works does it hand over to the
    /// generic rescue.
    /// </summary>
    private IBrush HyperlinkBrushFor(ArgbColor background)
    {
        if (Readable(_palette.Hyperlink, background))
        {
            return _palette.Hyperlink;
        }

        return Readable(_palette.HyperlinkAlternate, background)
            ? _palette.HyperlinkAlternate
            : Legible(_palette.Hyperlink, background);
    }

    private void DrawSelection(DrawingContext context, IBrush fill)
    {
        // Fill every selected rectangle (one, or several for a Cmd+click discontiguous selection),
        // each clipped to the visible viewport.
        foreach (var range in Selection.Ranges)
        {
            var shown = Highlighted(range);
            if (TryGetRegionRect(shown.Top, shown.Left, shown.Bottom, shown.Right, out var rect))
            {
                context.FillRectangle(fill, rect);
            }
        }
    }

    /// <summary>
    /// The rectangle a selected <paramref name="range"/> is <i>shown</i> as — itself, except when ⌘A has
    /// expanded to the whole sheet.
    ///
    /// <para>A whole-sheet selection is 16384 × 1048576 cells and is deliberately held as that rectangle (as
    /// Excel and Calc hold theirs), but copy and cut clamp it to the sheet's used range — otherwise there is
    /// no copy, only a hang. So the highlight has to clamp too: washing the entire grid promises the action
    /// will run over all of it, and the reader has no way to know it won't. Both go through
    /// <see cref="ClipboardBlock.CopyRange"/>, so what is lit and what is copied cannot drift apart.</para>
    ///
    /// <para>It costs a used-range walk per paint while such a selection is up. That is the state you are in
    /// for the two seconds between ⌘A⌘A and ⌘C, and it buys the selection telling the truth.</para>
    /// </summary>
    private CellRange Highlighted(CellRange range) =>
        Sheet is { } sheet ? ClipboardBlock.CopyRange(sheet, range) : range;

    /// <summary>The rectangle the selection is drawn (and its headers lit) over; for tests — the headless
    /// suite has no pixels to read the wash off.</summary>
    internal CellRange HighlightedSelectionForTest => Highlighted(Selection.Range);

    private void DrawActiveOutline(DrawingContext context, IPen pen)
    {
        // A merged active cell is outlined around its whole region, not just the anchor.
        if (Sheet?.FindMerge(Selection.ActiveRow, Selection.ActiveColumn) is { } merge)
        {
            if (TryGetRegionRect(merge.Range.Top, merge.Range.Left, merge.Range.Bottom, merge.Range.Right, out var mergeRect))
            {
                context.DrawRectangle(null, pen, mergeRect);
            }

            return;
        }

        if (TryGetCellRect(Selection.ActiveRow, Selection.ActiveColumn, out var rect))
        {
            context.DrawRectangle(null, pen, rect);
        }
    }

    private void DrawHeaders(DrawingContext context, IBrush headerBrush, IBrush textBrush, IPen gridPen)
    {
        // The headers light the same span the cells are washed over — the clamped one for an expanded ⌘A
        // (see Highlighted), so the two halves of the selection never disagree about how far it reaches.
        var lit = Highlighted(Selection.Range);

        // Corner.
        context.FillRectangle(headerBrush, new Rect(0, 0, HeaderWidth, HeaderHeight));

        var typeface = new Typeface(Cellestra.Core.Defaults.FontFamily);
        // Header labels scale with the zoom level, like the cell text, but stay legible at small zoom.
        double headerFontSize = Math.Clamp(11 * Zoom, 8, HeaderHeight - 4);

        // Clip each header strip to its own band so a partially-scrolled first column/row (drawn shifted
        // by its sub-cell offset) doesn't bleed its label into the corner.
        using (context.PushClip(new Rect(HeaderWidth, 0, Math.Max(0, Bounds.Width - HeaderWidth), HeaderHeight)))
        {
            foreach (var (col, x, w) in _visibleColumns)
            {
                var rect = new Rect(x, 0, w, HeaderHeight);
                bool active = col >= lit.Left && col <= lit.Right;
                context.FillRectangle(active ? _palette.ActiveHeaderFill : headerBrush, rect);
                context.DrawRectangle(null, gridPen, rect);
                DrawCentered(context, ColumnHeaderLabel(col), rect, typeface, textBrush, headerFontSize, rect.Width - 4);
            }
        }

        using (context.PushClip(new Rect(0, HeaderHeight, HeaderWidth, Math.Max(0, Bounds.Height - HeaderHeight))))
        {
            foreach (var (row, y, h) in _visibleRows)
            {
                var rect = new Rect(0, y, HeaderWidth, h);
                bool active = row >= lit.Top && row <= lit.Bottom;
                context.FillRectangle(active ? _palette.ActiveHeaderFill : headerBrush, rect);
                context.DrawRectangle(null, gridPen, rect);
                DrawCentered(context, (row + 1).ToString(CultureInfo.InvariantCulture), rect, typeface, textBrush, headerFontSize);
            }
        }
    }

    /// <summary>
    /// What the column-header strip says above <paramref name="column"/>: the table's own column title
    /// while you are scrolled inside that table's body, and the column's letter the rest of the time
    /// (including once you scroll past the table's last row, when there is no table any more).
    /// </summary>
    private string ColumnHeaderLabel(int column)
    {
        string letters = SheetDimensions.ColumnToLetters(column);
        if (Sheet is not { } sheet
            || FirstScrollableVisibleRow() is not { } topRow
            || StickyTableHeader.LabelRow(sheet, column, topRow) is not { } headerRow)
        {
            return letters;
        }

        // An unnamed header cell has nothing to say, so the letter stays — better than a blank strip.
        string title = CellDisplayText(sheet, headerRow, column);
        return title.Length > 0 ? title : letters;
    }

    /// <summary>
    /// The topmost row that actually scrolls, skipping any frozen band. Frozen rows sit above the fold
    /// permanently, so asking "what have I scrolled into?" of one always answers "nothing".
    /// </summary>
    private int? FirstScrollableVisibleRow()
    {
        int frozen = Sheet?.FrozenRows ?? 0;
        foreach (var (row, _, _) in _visibleRows)
        {
            if (row >= frozen)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>A cell's text exactly as the grid would draw it (formatted through its number format).</summary>
    private string CellDisplayText(Worksheet sheet, int row, int column)
    {
        var cell = sheet.Find(row, column);
        return cell is null ? string.Empty : NumberFormatter.Format(cell.Value, ResolveStyle(row, column).NumberFormat, Culture);
    }

    /// <summary>Test seam: the label the header strip shows above <paramref name="column"/> right now.</summary>
    internal string ColumnHeaderLabelForTest(int column) => ColumnHeaderLabel(column);

    private static void DrawCentered(DrawingContext context, string text, Rect rect, Typeface typeface, IBrush brush, double fontSize, double maxWidth = double.PositiveInfinity)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, brush)
        {
            // A table's column title can be far wider than its column; a letter never is.
            MaxTextWidth = double.IsPositiveInfinity(maxWidth) ? double.PositiveInfinity : Math.Max(1, maxWidth),
            Trimming = TextTrimming.CharacterEllipsis,
        };

        context.DrawText(formatted, new Point(
            rect.X + (rect.Width - formatted.Width) / 2,
            rect.Y + (rect.Height - formatted.Height) / 2));
    }

    private bool TryGetCellRect(int row, int column, out Rect rect)
    {
        double? x = null, y = null, w = null, h = null;
        foreach (var (c, cx, cw) in _visibleColumns)
        {
            if (c == column)
            {
                x = cx;
                w = cw;
                break;
            }
        }

        foreach (var (r, ry, rh) in _visibleRows)
        {
            if (r == row)
            {
                y = ry;
                h = rh;
                break;
            }
        }

        if (x is null || y is null)
        {
            rect = default;
            return false;
        }

        rect = new Rect(x.Value, y.Value, w!.Value, h!.Value);
        return true;
    }

    /// <summary>
    /// Test seam: whether the clipped-overflow chevron is drawn for a cell and, if so, on which side
    /// (true = right). Measures the cell as <see cref="DrawCellContent"/> does, then applies the shared
    /// <see cref="OverflowChevronSide"/> rule. Null when the text fits, wraps, or is a number (#####).
    /// Call after a frame has been laid out so the visible columns/rows exist.
    /// </summary>
    internal bool? OverflowMarkerSideForTest(int row, int column)
    {
        var sheet = Sheet;
        if (sheet is null || !TryGetCellRect(row, column, out var rect))
        {
            return null;
        }

        var cell = sheet.Find(row, column);
        if (cell is null || cell.Value.IsEmpty)
        {
            return null;
        }

        var style = ResolveStyle(row, column);
        string text = NumberFormatter.Format(cell.Value, style.NumberFormat, Culture);
        if (text.Length == 0)
        {
            return null;
        }

        bool numeric = cell.Value.IsNumber || cell.Value.IsBoolean || cell.Value.IsError;
        if (numeric || style.Alignment.WrapText)
        {
            return null; // numbers show #####; wrapped text grows the row instead of clipping
        }

        var typeface = new Typeface(
            style.Font.Family,
            style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);
        double fontSize = style.Font.SizePoints * 96.0 / 72.0 * Zoom;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);

        var horizontal = style.Alignment.Horizontal switch
        {
            SAlign.General => SAlign.Left,
            var h => h,
        };

        double available = rect.Width - 2 * CellPadX;
        if (horizontal == SAlign.Left && formatted.Width > available)
        {
            available += SpillRoom(sheet, row, column);
        }

        return OverflowChevronSide(formatted.Width, available, horizontal == SAlign.Right);
    }

    /// <summary>
    /// The on-screen rectangle the inline editor should occupy for the active cell: the combined
    /// merged region when the active cell is merged, otherwise just the single cell.
    /// </summary>
    private bool TryGetActiveCellRect(out Rect rect)
    {
        if (Sheet?.FindMerge(Selection.ActiveRow, Selection.ActiveColumn) is { } merge)
        {
            return TryGetRegionRect(merge.Range.Top, merge.Range.Left, merge.Range.Bottom, merge.Range.Right, out rect);
        }

        return TryGetCellRect(Selection.ActiveRow, Selection.ActiveColumn, out rect);
    }

    private static Color ToColor(ArgbColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>
    /// The decorations to draw on a cell: a hyperlink is always underlined (adding the font's own
    /// strikethrough if set); a plain cell uses just its font's underline/strikethrough.
    /// </summary>
    private static TextDecorationCollection? LinkDecorations(Cellestra.Core.Styling.Font font, bool isLink)
    {
        if (!isLink)
        {
            return Decorations(font);
        }

        var decorations = new TextDecorationCollection { TextDecorations.Underline[0] };
        if (font.Strikethrough)
        {
            decorations.Add(TextDecorations.Strikethrough[0]);
        }

        return decorations;
    }

    /// <summary>The text decorations for a font (underline and/or strikethrough), or null when it has neither.</summary>
    private static TextDecorationCollection? Decorations(Cellestra.Core.Styling.Font font)
    {
        if (!font.Underline && !font.Strikethrough)
        {
            return null;
        }

        var decorations = new TextDecorationCollection();
        if (font.Underline)
        {
            decorations.Add(TextDecorations.Underline[0]);
        }

        if (font.Strikethrough)
        {
            decorations.Add(TextDecorations.Strikethrough[0]);
        }

        return decorations;
    }
}
