using System;
using Avalonia;
using Avalonia.Media;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The fill handle (PRD §8.15): a small grip at the selection's bottom-right corner. Dragging it
/// copy-fills the selection across the dragged extent; double-clicking it fills down to match the
/// adjacent column's length (#7). Copy-fill only — no series extrapolation (a v1 non-goal). The
/// actual write is the undoable Core <see cref="FillCommand"/>.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private const double FillHandleSize = 7;

    private bool _filling;
    private CellRange _fillSource;
    private CellRange? _fillTarget;

    private bool TryGetFillHandleRect(out Rect rect)
    {
        rect = default;
        if (_session is null || _editing)
        {
            return false;
        }

        if (!TryGetRegionRect(Selection.Top, Selection.Left, Selection.Bottom, Selection.Right, out var selection))
        {
            return false;
        }

        rect = new Rect(
            selection.Right - (FillHandleSize / 2),
            selection.Bottom - (FillHandleSize / 2),
            FillHandleSize,
            FillHandleSize);
        return true;
    }

    /// <summary>Start a fill drag if the press landed on the handle. Returns true if handled.</summary>
    private bool TryBeginFill(Point point)
    {
        if (!TryGetFillHandleRect(out var handle) || !handle.Contains(point))
        {
            return false;
        }

        _filling = true;
        _fillSource = Selection.Range;
        _fillTarget = _fillSource;
        return true;
    }

    private void UpdateFill(Point point)
    {
        if (!HitTest(point, out int row, out int column, out _, out _))
        {
            return;
        }

        var src = _fillSource;
        int rowsBeyond = row > src.Bottom ? row - src.Bottom : (row < src.Top ? row - src.Top : 0);
        int columnsBeyond = column > src.Right ? column - src.Right : (column < src.Left ? column - src.Left : 0);

        // Fill in the single dominant axis, like Excel — a drag is either vertical or horizontal.
        if (Math.Abs(rowsBeyond) >= Math.Abs(columnsBeyond))
        {
            _fillTarget = rowsBeyond switch
            {
                > 0 => Range(src.Left, src.Top, src.Right, row),
                < 0 => Range(src.Left, row, src.Right, src.Bottom),
                _ => src,
            };
        }
        else
        {
            _fillTarget = columnsBeyond switch
            {
                > 0 => Range(src.Left, src.Top, column, src.Bottom),
                < 0 => Range(column, src.Top, src.Right, src.Bottom),
                _ => src,
            };
        }

        Redraw();
    }

    private void CommitFill()
    {
        if (_session is not null && _fillTarget is { } target && !SameRange(target, _fillSource))
        {
            _session.Execute(new FillCommand(_session.ActiveSheet.Name, _fillSource, target));
            Selection.SetRange(target.Top, target.Left, target.Bottom, target.Right);
            RaiseSelectionChanged();
        }

        _filling = false;
        _fillTarget = null;
    }

    /// <summary>Double-click the handle: fill the selection down to the adjacent column's data length.</summary>
    private void FillDownToAdjacent()
    {
        var sheet = Sheet;
        if (_session is null || sheet is null)
        {
            return;
        }

        var src = Selection.Range;
        int neighbor = src.Left - 1 >= 0 ? src.Left - 1 : src.Right + 1;
        int bottom = src.Bottom;
        for (int row = src.Bottom + 1; row < SheetDimensions.MaxRows; row++)
        {
            var cell = sheet.Find(row, neighbor);
            if (cell is null || cell.Value.IsEmpty)
            {
                break;
            }

            bottom = row;
        }

        if (bottom > src.Bottom)
        {
            var target = Range(src.Left, src.Top, src.Right, bottom);
            _session.Execute(new FillCommand(_session.ActiveSheet.Name, src, target));
            Selection.SetRange(target.Top, target.Left, target.Bottom, target.Right);
            RaiseSelectionChanged();
        }
    }

    private void DrawFillHandle(DrawingContext context)
    {
        if (_filling || !TryGetFillHandleRect(out var rect))
        {
            return;
        }

        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x7D, 0xE1)), rect);
        context.DrawRectangle(null, new Pen(Brushes.White, 1), rect);
    }

    private void DrawFillPreview(DrawingContext context)
    {
        if (!_filling || _fillTarget is not { } target ||
            !TryGetRegionRect(target.Top, target.Left, target.Bottom, target.Right, out var rect))
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x7D, 0xE1)), 1)
        {
            DashStyle = new DashStyle(new double[] { 3, 2 }, 0),
        };
        context.DrawRectangle(null, pen, rect);
    }

    private static CellRange Range(int left, int top, int right, int bottom) =>
        new(new CellAddress(left, top), new CellAddress(right, bottom));

    private static bool SameRange(CellRange a, CellRange b) =>
        a.Top == b.Top && a.Left == b.Left && a.Bottom == b.Bottom && a.Right == b.Right;
}
