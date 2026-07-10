using Avalonia;
using Avalonia.Input;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

public sealed partial class SpreadsheetGrid
{
    // ---- Hit testing & input ---------------------------------------------

    private bool HitTest(Point point, out int row, out int column, out bool rowHeader, out bool columnHeader)
    {
        row = column = -1;
        rowHeader = point.X < HeaderWidth;
        columnHeader = point.Y < HeaderHeight;

        foreach (var (c, x, w) in _visibleColumns)
        {
            if (point.X >= x && point.X < x + w)
            {
                column = c;
                break;
            }
        }

        foreach (var (r, y, h) in _visibleRows)
        {
            if (point.Y >= y && point.Y < y + h)
            {
                row = r;
                break;
            }
        }

        if (column < 0 && _visibleColumns.Count > 0)
        {
            column = _visibleColumns[^1].Index;
        }

        if (row < 0 && _visibleRows.Count > 0)
        {
            row = _visibleRows[^1].Index;
        }

        return row >= 0 && column >= 0;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);

        // A point-mode press was already consumed in the tunnel preview (it inserts a reference and
        // keeps the editor focused); nothing more to do here.
        if (e.Handled)
        {
            return;
        }

        // Right-click drives the radial command menu and the context menu (see SpreadsheetGrid.Radial):
        // a quick click raises the context menu, a press-and-hold raises the radial. Don't let it start
        // a selection. Commit any in-progress edit first so it acts on a settled cell, and focus the grid
        // so typing works once it closes. e.Handled stops the framework's own pointer context request —
        // this gesture owns pointer-driven menus.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (IsInlineEditing)
            {
                CommitEdit(0, 0);
            }

            Focus();
            BeginRightClickGesture(point, e.Pointer);
            e.Handled = true;
            return;
        }

        Focus();
        if (IsInlineEditing)
        {
            CommitEdit(0, 0);
        }

        _tabOriginColumn = null; // a click breaks any Tab-entry sequence (#19)

        // When the Format Painter is armed, a press starts a paint gesture (click one cell or drag a
        // range), taking precedence over normal selection — release paints the stamp and disarms.
        if (TryBeginPaint(e, point))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleChromeClick(point))
        {
            e.Handled = true;
            return;
        }

        // Double-clicking a column header's right border autofits the column to its content (#23).
        if (e.ClickCount == 2 && TryHitColumnBorder(point, out int borderColumn))
        {
            AutofitColumn(borderColumn);
            e.Handled = true;
            return;
        }

        // Drag on a header border resizes: a column by its right border, a row by its bottom border.
        // (A double-click on the border was already handled as autofit above.)
        if (TryBeginResize(e, point))
        {
            e.Handled = true;
            return;
        }

        // The fill handle takes precedence over cell selection at the selection's bottom-right.
        if (TryGetFillHandleRect(out var handle) && handle.Contains(point))
        {
            if (e.ClickCount == 2)
            {
                FillDownToAdjacent();
            }
            else
            {
                TryBeginFill(point);
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (!HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader))
        {
            return;
        }

        // Cmd-click a hyperlinked cell opens its link with the OS default handler, leaving the
        // selection untouched (so the link gesture never breaks the discontiguous-select gesture
        // for non-linked cells). Single click only; headers and double-clicks fall through.
        if (e.ClickCount == 1 && !rowHeader && !columnHeader
            && e.KeyModifiers.HasFlag(KeyModifiers.Meta) && TryActivateHyperlink(row, column))
        {
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            SelectCellOrMerge(row, column);
            BeginEdit(null);
            return;
        }

        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Meta); // Cmd: add a disjoint range (#27/#28)
        if (rowHeader && !columnHeader)
        {
            if (extend)
            {
                Selection.ExtendRowBand(row); // Shift+click: select every row from the anchor to here (#3)
            }
            else
            {
                SelectRowOrColumn(additive, row, 0, row, SheetDimensions.MaxColumns - 1);
            }
        }
        else if (columnHeader && !rowHeader)
        {
            if (extend)
            {
                Selection.ExtendColumnBand(column);
            }
            else
            {
                SelectRowOrColumn(additive, 0, column, SheetDimensions.MaxRows - 1, column);
            }
        }
        else if (extend)
        {
            Selection.ExtendTo(row, column);
        }
        else
        {
            if (additive)
            {
                Selection.AddPoint(row, column);
            }
            else
            {
                SelectCellOrMerge(row, column);
            }

            _selecting = true;
            e.Pointer.Capture(this);
        }

        RaiseSelectionChanged();
    }

    /// <summary>What a right-click landed on, so the host can show the matching context menu.</summary>
    internal enum ContextTarget
    {
        None,
        Cell,
        RowHeader,
        ColumnHeader,
    }

    /// <summary>
    /// Prepare the selection for a context menu at <paramref name="point"/> and report what it hit.
    /// A cell/row/column outside the current selection becomes selected (a whole row/column for a
    /// header); a click inside the existing selection keeps it, so the menu acts on the whole range.
    /// </summary>
    internal ContextTarget SelectForContext(Point point)
    {
        if (!HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader))
        {
            return ContextTarget.None;
        }

        if (rowHeader && columnHeader)
        {
            return ContextTarget.None; // the top-left corner
        }

        if (rowHeader)
        {
            // Keep the selection only if it's already whole rows covering this one (so right-clicking
            // one of several selected rows deletes them all); otherwise select just this full row.
            bool wholeRows = Selection.Left == 0 && Selection.Right == SheetDimensions.MaxColumns - 1;
            if (!(wholeRows && row >= Selection.Top && row <= Selection.Bottom))
            {
                Selection.SetRange(row, 0, row, SheetDimensions.MaxColumns - 1);
                RaiseSelectionChanged();
            }

            return ContextTarget.RowHeader;
        }

        if (columnHeader)
        {
            bool wholeColumns = Selection.Top == 0 && Selection.Bottom == SheetDimensions.MaxRows - 1;
            if (!(wholeColumns && column >= Selection.Left && column <= Selection.Right))
            {
                Selection.SetRange(0, column, SheetDimensions.MaxRows - 1, column);
                RaiseSelectionChanged();
            }

            return ContextTarget.ColumnHeader;
        }

        bool inSelection = row >= Selection.Top && row <= Selection.Bottom && column >= Selection.Left && column <= Selection.Right;
        if (!inSelection)
        {
            Selection.MoveTo(row, column);
            RaiseSelectionChanged();
        }

        return ContextTarget.Cell;
    }

    private void SelectRowOrColumn(bool additive, int top, int left, int bottom, int right)
    {
        if (additive)
        {
            Selection.AddRange(top, left, bottom, right);
        }
        else
        {
            Selection.SetRange(top, left, bottom, right);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        // While a right-button radial gesture is in flight the pointer drives the radial (its direction
        // picks a wedge); suppress the usual hover/selection handling until the button is released.
        if (_radialActive)
        {
            UpdateRadialHover(point);
            return;
        }

        if (_radialPending)
        {
            return;
        }

        if (_pointDragging)
        {
            UpdatePointDrag(point); // extend the point-mode reference into a range as the pointer drags
            return;
        }

        if (IsResizing)
        {
            UpdateResize(point);
            return;
        }

        if (_filling)
        {
            UpdateFill(point);
            return;
        }

        if (_painting)
        {
            UpdatePaint(point);
            return;
        }

        if (IsFormatPainterArmed)
        {
            UpdatePaintCursor(); // keep the paintbrush cursor; don't let resize-hover override it
            return;
        }

        if (!_selecting)
        {
            // Hovering a header border shows the resize cursor; elsewhere, the default arrow.
            UpdateResizeCursor(point);
            UpdateHoverTip(point); // error-cell reason (task 12) or "⌘-click to open …" for a link
            return;
        }

        if (HitTest(point, out int row, out int column, out _, out _))
        {
            Selection.ExtendTo(row, column);
            RaiseSelectionChanged();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // Finish a right-button radial gesture: run the highlighted command, or fall back to the
        // context menu on a quick click. HandleRadialRelease snapshots and tears down the gesture
        // before acting, so releasing the capture (which fires CaptureLost → CancelRadial) is a no-op.
        if (_radialActive || _radialPending)
        {
            HandleRadialRelease();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_pointDragging)
        {
            // The point-mode drag is done; the inserted range stays in the formula. Keep
            // _pointInsertStart set so a following click replaces it, like Excel.
            _pointDragging = false;
            e.Pointer.Capture(null);
            return;
        }

        if (IsResizing)
        {
            EndResize();
            e.Pointer.Capture(null);
            return;
        }

        if (_filling)
        {
            CommitFill();
        }

        if (_painting)
        {
            CommitPaint(); // paint the stamp over the dragged (or single-cell) range, then disarm
        }

        _selecting = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHoverTip(); // don't leave a stale tooltip when the pointer leaves the grid
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // Shift maps a vertical wheel onto the horizontal axis for mouse users; a trackpad already
        // delivers a real Delta.X, so leave that alone.
        double dx = e.Delta.X;
        double dy = e.Delta.Y;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && dx == 0)
        {
            dx = dy;
            dy = 0;
        }

        if (ApplyWheelScroll(dx, dy))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Scroll by raw wheel deltas, converted to pixels rather than whole cells, so a trackpad (which
    /// delivers many small high-resolution deltas) glides smoothly and a mouse wheel moves a gentle,
    /// predictable amount. Both axes move at once for two-finger gestures. A positive delta means
    /// "toward the top/left", i.e. a smaller scroll offset. Returns whether anything moved.
    /// </summary>
    private bool ApplyWheelScroll(double deltaX, double deltaY)
    {
        var sheet = Sheet;
        if (sheet is null || (deltaX == 0 && deltaY == 0))
        {
            return false;
        }

        if (deltaY != 0)
        {
            ScrollRowOffsetForTest -= deltaY * WheelPixelsPerNotch;
            NormalizeVerticalScroll(sheet);
        }

        if (deltaX != 0)
        {
            ScrollColumnOffsetForTest -= deltaX * WheelPixelsPerNotch;
            NormalizeHorizontalScroll(sheet);
        }

        Redraw();
        InvalidateArrange(); // keep the scroll bars in step with the new scroll position
        return true;
    }
}
