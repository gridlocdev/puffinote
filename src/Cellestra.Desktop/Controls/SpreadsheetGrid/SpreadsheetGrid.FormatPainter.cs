using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Drag-to-paint Format Painter: a direct, in-grid way to apply the captured formatting "stamp"
/// (see <see cref="Cellestra.Core.Editing.FormatBlock"/>). While the painter is armed on the session
/// (<see cref="Services.WorkbookSession.IsFormatPainterArmed"/>) — triggered by the
/// <c>edit.formatPainter</c> command — the grid shows a paintbrush-style cursor and lets the user
/// click a single cell or drag across a range; on release it paints the stamp over that range as one
/// undoable edit (<see cref="FormatBlock.ApplyTo"/>) and disarms (single-shot, matching the two-step
/// command model). Esc cancels without painting. The live target is shown with the same dashed
/// drag-rectangle used by the fill handle.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    // No standard "paintbrush" cursor exists in Avalonia's StandardCursorType, and a custom bitmap
    // cursor is heavier than this polish warrants; Cross reads as "pick the cells to paint" and is the
    // closest sensible standard cursor. Swapping in a bitmap later only touches this field.
    private static readonly Cursor PaintCursor = new(StandardCursorType.Cross);

    private bool _painting;          // a paint drag is in progress (pointer down while armed)
    private int _paintAnchorRow;     // the cell the paint drag started on
    private int _paintAnchorColumn;
    private CellRange? _paintTarget; // the live rectangle the drag covers

    /// <summary>True when the Format Painter is armed for this grid's session.</summary>
    private bool IsFormatPainterArmed => _session?.IsFormatPainterArmed ?? false;

    /// <summary>Hook the session's arm/disarm so the cursor and any in-flight drag stay in step. Called from <see cref="Attach"/>.</summary>
    private void WireFormatPainter(Services.WorkbookSession session)
    {
        session.FormatPainterArmedChanged += (_, _) => OnFormatPainterArmedChanged();
    }

    private void OnFormatPainterArmedChanged()
    {
        if (!IsFormatPainterArmed)
        {
            _painting = false;
            _paintTarget = null;
        }

        UpdatePaintCursor();
        Redraw();
    }

    /// <summary>Show the paintbrush cursor while armed; restore the normal hover cursor otherwise.</summary>
    private void UpdatePaintCursor()
    {
        if (IsFormatPainterArmed)
        {
            _cursorType = StandardCursorType.Cross;
            Cursor = PaintCursor;
        }
        else if (_cursorType == StandardCursorType.Cross)
        {
            // Leaving the armed state: drop back to the default arrow (hover code re-derives resize cursors).
            _cursorType = StandardCursorType.Arrow;
            Cursor = Cursor.Default;
        }
    }

    /// <summary>
    /// Start a paint gesture if the painter is armed. Returns true if handled (so the press doesn't
    /// fall through to normal selection). A single click that never moves still paints that one cell.
    /// </summary>
    private bool TryBeginPaint(PointerPressedEventArgs e, Point point)
    {
        if (!IsFormatPainterArmed || !HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader))
        {
            return false;
        }

        if (rowHeader || columnHeader)
        {
            return false; // headers aren't paint targets; let normal handling select the row/column
        }

        _painting = true;
        _paintAnchorRow = row;
        _paintAnchorColumn = column;
        _paintTarget = FormatBlock.PaintTarget(row, column, row, column);
        e.Pointer.Capture(this);
        Redraw();
        return true;
    }

    /// <summary>Extend the live paint rectangle to the cell under the pointer.</summary>
    private void UpdatePaint(Point point)
    {
        if (!HitTest(point, out int row, out int column, out _, out _))
        {
            return;
        }

        _paintTarget = FormatBlock.PaintTarget(_paintAnchorRow, _paintAnchorColumn, row, column);
        Redraw();
    }

    /// <summary>Apply the stamp over the painted rectangle as one undoable edit, then disarm (single-shot).</summary>
    private void CommitPaint()
    {
        if (_session is { } session && session.FormatStamp is { } stamp && _paintTarget is { } target)
        {
            session.Execute(stamp.ApplyTo(session.ActiveSheet.Name, target));
            Selection.SetRange(target.Top, target.Left, target.Bottom, target.Right);
            RaiseSelectionChanged();
        }

        _painting = false;
        _paintTarget = null;
        _session?.DisarmFormatPainter(); // raises FormatPainterArmedChanged → clears the cursor
    }

    /// <summary>Esc while armed cancels without painting; returns true if it consumed the key.</summary>
    private bool TryCancelPaint()
    {
        if (!IsFormatPainterArmed)
        {
            return false;
        }

        _painting = false;
        _paintTarget = null;
        _session?.DisarmFormatPainter();
        return true;
    }

    /// <summary>Live feedback: the dashed rectangle over the cells that will be painted (matches the fill preview).</summary>
    private void DrawPaintPreview(DrawingContext context)
    {
        if (!_painting || _paintTarget is not { } target ||
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
}
