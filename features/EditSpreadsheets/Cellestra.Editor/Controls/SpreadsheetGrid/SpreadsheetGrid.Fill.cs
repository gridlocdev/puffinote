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

        _fillTarget = ComputeFillTarget(_fillSource, row, column);
        Redraw();
    }

    /// <summary>
    /// The fill rectangle for a drag to (<paramref name="row"/>, <paramref name="column"/>): the
    /// bounding box of the source and the cursor, so the drag extends in <b>both</b> axes at once
    /// (the source tiles to fill the whole box, per <see cref="FillCommand"/>).
    /// </summary>
    internal static CellRange ComputeFillTarget(CellRange src, int row, int column) => Range(
        Math.Min(src.Left, column),
        Math.Min(src.Top, row),
        Math.Max(src.Right, column),
        Math.Max(src.Bottom, row));

    private void CommitFill()
    {
        if (_session is not null && _fillTarget is { } target && !SameRange(target, _fillSource))
        {
            var command = new FillCommand(_session.ActiveSheet.Name, _fillSource, target);
            if (TryEnforceValidation(command)) // abort the whole fill on a Stop-rule violation (#B)
            {
                _session.Execute(command);
                Selection.SetRange(target.Top, target.Left, target.Bottom, target.Right);
                RaiseSelectionChanged();
            }
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
            var command = new FillCommand(_session.ActiveSheet.Name, src, target);
            if (TryEnforceValidation(command)) // abort the whole fill on a Stop-rule violation (#B)
            {
                _session.Execute(command);
                Selection.SetRange(target.Top, target.Left, target.Bottom, target.Right);
                RaiseSelectionChanged();
            }
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
