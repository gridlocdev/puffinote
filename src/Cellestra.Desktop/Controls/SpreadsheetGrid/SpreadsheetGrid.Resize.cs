using System;
using Avalonia;
using Avalonia.Input;
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
    private readonly Dictionary<int, double> _rowFontPoints = new();

    /// <summary>The largest font size (points) used by any cell in <paramref name="row"/>, or 0 if none.</summary>
    private double TallestFontPoints(Worksheet sheet, int row) =>
        _rowFontPoints.TryGetValue(row, out double pts) ? pts : 0;

    /// <summary>Recompute the row→tallest-font map for the active sheet (one sparse-store scan per frame).</summary>
    private void RebuildRowFontMap(Worksheet sheet)
    {
        _rowFontPoints.Clear();
        foreach (var (row, _, cell) in sheet.EnumerateCells())
        {
            if (cell.Value.IsEmpty)
            {
                continue;
            }

            double pts = _session!.Workbook.Styles.Get(cell.StyleId).Font.SizePoints;
            if (!_rowFontPoints.TryGetValue(row, out double current) || pts > current)
            {
                _rowFontPoints[row] = pts;
            }
        }
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

        // Otherwise grow the row to fit the tallest font any of its cells uses, so a larger font
        // size never clips/overlaps the next row (task 3). The default 20px row is the floor.
        return RowHeightCalculator.Effective(sheet.GetRowHeight(row), TallestFontPoints(sheet, row), CellPadY);
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
            double delta = (point.X - _dragStartPointer) / _zoom; // pointer is zoomed; widths are model-space
            _dragSize = Math.Max(MinColumnWidth, _dragStartSize + delta);
        }
        else if (_dragRow is not null)
        {
            double delta = (point.Y - _dragStartPointer) / _zoom;
            _dragSize = Math.Max(MinRowHeight, _dragStartSize + delta);
        }

        Redraw();
    }

    /// <summary>If the OS takes the pointer mid-drag (window deactivation, a popup), abandon the drag cleanly.</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (IsResizing)
        {
            _dragColumn = null;
            _dragRow = null;
            Redraw();
        }

        _selecting = false;
        _filling = false;
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
