using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Drag-to-resize on the header borders: hovering a column's right border (or a row's bottom border)
/// shows the matching resize cursor, and dragging resizes that column/row live, committing one
/// undoable <see cref="EditCommands.SetColumnWidth"/>/<see cref="EditCommands.SetRowHeight"/> on
/// release. Double-clicking the border still autofits (see the Autofit partial). The drag preview
/// is purely visual via <see cref="EffectiveColumnWidth"/>/<see cref="EffectiveRowHeight"/> — the
/// model only changes on release, so undo restores the prior size in one step.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private const double MinColumnWidth = 16;
    private const double MinRowHeight = 8;

    private static readonly Cursor ResizeColumnCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor ResizeRowCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor FillCursor = new(StandardCursorType.DragCopy);

    private int? _dragColumn;          // the column whose right border is being dragged
    private int? _dragRow;             // the row whose bottom border is being dragged
    private double _dragStartPointer;  // pointer X (column) or Y (row) in device px at press
    private double _dragStartSize;     // the original width/height in model space
    private double _dragSize;          // the live previewed width/height in model space
    private StandardCursorType _cursorType = StandardCursorType.Arrow;

    private bool IsResizing => _dragColumn is not null || _dragRow is not null;

    // Per-frame cache of each row's tallest font (in points), so a layout pass scans the sparse cell
    // store only once instead of once per visible row. Rebuilt at the start of each BuildVisible.
    private readonly Dictionary<int, double> _rowFontPoints = [];

    // Per-frame cache of each row's tallest wrapped-cell height, in model-space pixels (zoom=1), so a
    // Wrap-Text cell grows its row to fit every wrapped line (task 6). Rebuilt alongside _rowFontPoints.
    private readonly Dictionary<int, double> _rowWrappedPixels = [];

    /// <summary>The largest font size (points) used by any cell in <paramref name="row"/>, or 0 if none.</summary>
    private double TallestFontPoints(int row) =>
        _rowFontPoints.TryGetValue(row, out double pts) ? pts : 0;

    /// <summary>The tallest wrapped-cell height (model-space px) in <paramref name="row"/>, or 0 if none wrap.</summary>
    private double WrappedRowPixels(int row) =>
        _rowWrappedPixels.TryGetValue(row, out double px) ? px : 0;

    /// <summary>Recompute the row→tallest-font (and row→wrapped-height) maps for the active sheet (one sparse-store scan per frame).</summary>
    private void RebuildRowFontMap(Worksheet sheet)
    {
        _rowFontPoints.Clear();
        _rowWrappedPixels.Clear();
        foreach (var (row, col, cell) in sheet.EnumerateCells())
        {
            if (cell.Value.IsEmpty)
            {
                continue;
            }

            var style = _session!.Workbook.Styles.Get(cell.StyleId);
            double pts = style.Font.SizePoints;
            if (!_rowFontPoints.TryGetValue(row, out double current) || pts > current)
            {
                _rowFontPoints[row] = pts;
            }

            // A Wrap-Text cell needs a row tall enough for every wrapped line at this column's width.
            bool numeric = cell.Value.IsNumber || cell.Value.IsBoolean || cell.Value.IsError;
            if (style.Alignment.WrapText && !numeric && !sheet.IsColumnHidden(col))
            {
                double wrapped = MeasureWrappedRowHeight(sheet, cell, style, row, col);
                if (!_rowWrappedPixels.TryGetValue(row, out double existing) || wrapped > existing)
                {
                    _rowWrappedPixels[row] = wrapped;
                }
            }
        }
    }

    /// <summary>
    /// Measure how tall a Wrap-Text cell must be (model-space px, zoom=1) so its text wraps to the
    /// column width without clipping. Uses the same metrics as the render path so what's measured is
    /// what's drawn. A cell inside a merge wraps to the merge's full width, not one column.
    /// </summary>
    private double MeasureWrappedRowHeight(Worksheet sheet, Cell cell, Cellestra.Core.Styling.CellStyle style, int row, int column)
    {
        string text = Cellestra.Core.Styling.NumberFormatter.Format(cell.Value, style.NumberFormat, Culture);
        if (text.Length == 0)
        {
            return 0;
        }

        // A cell covered by (but not anchoring) a merge is drawn by the anchor — don't grow its row.
        var merge = sheet.FindMerge(row, column);
        if (merge is not null && (merge.Range.Top != row || merge.Range.Left != column))
        {
            return 0;
        }

        // Multi-row merges already provide their own vertical room; only a single-row merge (or a plain
        // cell) needs its row grown. Use the full merged width so the line count matches what's drawn.
        double widthModel = sheet.GetColumnWidth(column);
        if (merge is not null)
        {
            if (merge.Range.Top != merge.Range.Bottom)
            {
                return 0; // a multi-row merge already has the vertical room
            }

            widthModel = 0;
            for (int c = merge.Range.Left; c <= merge.Range.Right; c++)
            {
                widthModel += sheet.GetColumnWidth(c);
            }
        }

        var typeface = new Typeface(
            style.Font.Family,
            style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);
        double fontSize = style.Font.SizePoints * 96.0 / 72.0; // model space (zoom applied later)
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.Black)
        {
            MaxTextWidth = Math.Max(1, widthModel - 2 * CellPadX),
            Trimming = TextTrimming.None,
        };

        return Math.Ceiling(formatted.Height + 2 * CellPadY);
    }

    /// <summary>
    /// Test seam: the effective model-space height of a row (the floor, grown for the tallest font and
    /// any wrapped cell), after rebuilding the per-row maps from the active sheet. Lets a headless test
    /// assert that wrapping a long string grows the row without driving a full render.
    /// </summary>
    internal double MeasuredRowHeightForTest(int row)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return 0;
        }

        RebuildRowFontMap(sheet);
        return EffectiveRowHeight(sheet, row);
    }

    private double EffectiveColumnWidth(Worksheet sheet, int column) =>
        _dragColumn == column ? _dragSize : sheet.GetColumnWidth(column);

    private double EffectiveRowHeight(Worksheet sheet, int row)
    {
        // While dragging a row's border, show the raw previewed size so the drag reads true.
        if (_dragRow == row)
        {
            return _dragSize;
        }

        // Otherwise grow the row to fit the tallest font any of its cells uses (task 3) and the tallest
        // wrapped cell (task 6), so larger fonts or wrapped text never clip/overlap the next row. The
        // default 20px row is the floor.
        return RowHeightCalculator.Effective(
            sheet.GetRowHeight(row), TallestFontPoints(row), CellPadY, WrappedRowPixels(row));
    }

    /// <summary>Hover feedback: the copy cursor over the fill handle, a resize cursor over a header border, else the arrow.</summary>
    private void UpdateResizeCursor(Point point)
    {
        var desired = TryGetFillHandleRect(out var handle) && handle.Contains(point) ? StandardCursorType.DragCopy
            : TryHitColumnBorder(point, out _) ? StandardCursorType.SizeWestEast
            : TryHitRowBorder(point, out _) ? StandardCursorType.SizeNorthSouth
            : StandardCursorType.Arrow;

        if (desired == _cursorType)
        {
            return;
        }

        _cursorType = desired;
        Cursor = desired switch
        {
            StandardCursorType.SizeWestEast => ResizeColumnCursor,
            StandardCursorType.SizeNorthSouth => ResizeRowCursor,
            StandardCursorType.DragCopy => FillCursor,
            _ => Cursor.Default,
        };
    }

    private bool TryHitRowBorder(Point point, out int row)
    {
        row = -1;
        if (point.X >= HeaderWidth)
        {
            return false; // only the row-header strip
        }

        foreach (var (index, y, h) in _visibleRows)
        {
            if (Math.Abs(point.Y - (y + h)) <= BorderGrab)
            {
                row = index;
                return true;
            }
        }

        return false;
    }

    private bool TryBeginResize(PointerPressedEventArgs e, Point point)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return false;
        }

        if (TryHitColumnBorder(point, out int column))
        {
            _dragColumn = column;
            _dragStartPointer = point.X;
            _dragStartSize = _dragSize = sheet.GetColumnWidth(column);
            e.Pointer.Capture(this);
            return true;
        }

        if (TryHitRowBorder(point, out int row))
        {
            _dragRow = row;
            _dragStartPointer = point.Y;
            _dragStartSize = _dragSize = sheet.GetRowHeight(row);
            e.Pointer.Capture(this);
            return true;
        }

        return false;
    }

    private void UpdateResize(Point point)
    {
        if (_dragColumn is not null)
        {
            double delta = (point.X - _dragStartPointer) / Zoom; // pointer is zoomed; widths are model-space
            _dragSize = Math.Max(MinColumnWidth, _dragStartSize + delta);
        }
        else if (_dragRow is not null)
        {
            double delta = (point.Y - _dragStartPointer) / Zoom;
            _dragSize = Math.Max(MinRowHeight, _dragStartSize + delta);
        }

        Redraw();
    }

    /// <summary>If the OS takes the pointer mid-drag (window deactivation, a popup), abandon the drag cleanly.</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelRadial(); // a radial gesture can't continue without the pointer — abandon it cleanly
        if (IsResizing)
        {
            _dragColumn = null;
            _dragRow = null;
            Redraw();
        }

        _selecting = false;
        _filling = false;
        if (_painting)
        {
            // Pointer yanked mid-paint (window deactivated, popup): abandon without painting, stay armed.
            _painting = false;
            _paintTarget = null;
            Redraw();
        }
    }

    private void EndResize()
    {
        var sheet = Sheet;
        // Commit one undoable edit only if the size actually changed; a click without a drag is a no-op.
        if (sheet is not null && _session is not null && Math.Abs(_dragSize - _dragStartSize) > 0.5)
        {
            if (_dragColumn is int column)
            {
                _session.Execute(EditCommands.SetColumnWidth(sheet.Name, column, _dragSize));
            }
            else if (_dragRow is int row)
            {
                _session.Execute(EditCommands.SetRowHeight(sheet.Name, row, _dragSize));
            }
        }

        _dragColumn = null;
        _dragRow = null;
        Redraw();
    }
}
