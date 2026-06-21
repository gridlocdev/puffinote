using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Point mode (#17): while typing a formula, clicking another cell — or pressing the arrow keys —
/// inserts that cell's reference into the formula at the caret instead of moving the selection.
/// Consecutive pokes (with no typing in between) replace the just-inserted reference, so you can
/// move around to adjust it; type an operator (or <c>:</c>) and the next poke appends a new
/// reference. Holding Shift while arrowing extends the reference into a range as <c>A1:B2</c>.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private int _pointInsertStart = -1; // start of the last poke-inserted reference, or -1
    private int _pointInsertLength;
    private string? _programmaticText;   // the text we set ourselves, so its TextChanged isn't treated as typing
    private bool _barEcho;               // true only when _programmaticText came FROM the formula bar (suppress the bar echo)

    // Keyboard point mode: the cell the arrows are currently pointing at, plus the range anchor.
    private bool _pointKeyboard;
    private int _pointRow, _pointColumn, _pointAnchorRow, _pointAnchorColumn;

    // Mouse point mode: a drag started on a cell while composing a formula extends the just-inserted
    // reference into a range (A1:B2), like dragging across cells in Excel's point mode (task 1).
    private bool _pointDragging;
    private int _pointDragAnchorRow, _pointDragAnchorColumn;

    // The anchor corner of the last clicked reference, so a following Shift+click extends it into a
    // range from there — just like Shift+click extends a normal grid selection from its anchor.
    private bool _pointHasClickAnchor;
    private int _pointClickAnchorRow, _pointClickAnchorColumn;

    // The cell/range the point poke currently targets, drawn with the normal selection chrome so it's
    // obvious what's being picked while composing a formula (the coloured ref outline alone is easy to
    // miss, and only appears once the whole formula parses). Cleared when typing resumes.
    private bool _pointHasTarget;
    private CellRange _pointTarget;

    private void ResetPointMode()
    {
        _pointInsertStart = -1;
        _pointKeyboard = false;
        _pointDragging = false;
        _pointHasTarget = false;
        _pointHasClickAnchor = false;
    }

    /// <summary>The A1 reference for a cell, or a normalized A1:B2 range when the two corners differ.</summary>
    private static string RangeReferenceText(int anchorRow, int anchorColumn, int row, int column)
    {
        int top = Math.Min(anchorRow, row), bottom = Math.Max(anchorRow, row);
        int left = Math.Min(anchorColumn, column), right = Math.Max(anchorColumn, column);
        return top == bottom && left == right
            ? new CellAddress(left, top).ToString()
            : new CellAddress(left, top) + ":" + new CellAddress(right, bottom);
    }

    /// <summary>Test seam: the cell/range the point poke currently draws normal selection chrome over, or null.</summary>
    internal CellRange? PointTargetForTest => _editing && _pointHasTarget ? _pointTarget : null;

    /// <summary>Mark the cell/range the latest poke points at so it gets the normal selection chrome.</summary>
    private void SetPointTarget(int top, int left, int bottom, int right) =>
        SetPointTarget(new CellRange(new CellAddress(left, top), new CellAddress(right, bottom)));

    private void SetPointTarget(CellRange range)
    {
        _pointTarget = range;
        _pointHasTarget = true;
    }

    /// <summary>
    /// Paint the point-mode target with the same translucent fill the grid uses for a normal
    /// selection. Drawn under the cells (like <see cref="DrawSelection"/>) so it tints the band
    /// rather than the text.
    /// </summary>
    private void DrawPointSelectionFill(DrawingContext context)
    {
        if (_editing && _pointHasTarget
            && TryGetRegionRect(_pointTarget.Top, _pointTarget.Left, _pointTarget.Bottom, _pointTarget.Right, out var rect))
        {
            context.FillRectangle(_palette.SelectionFill, rect);
        }
    }

    /// <summary>Outline the point-mode target with the active-cell pen, drawn on top of the cells.</summary>
    private void DrawPointSelectionOutline(DrawingContext context)
    {
        if (_editing && _pointHasTarget
            && TryGetRegionRect(_pointTarget.Top, _pointTarget.Left, _pointTarget.Bottom, _pointTarget.Right, out var rect))
        {
            context.DrawRectangle(null, _palette.ActiveOutline, rect);
        }
    }

    /// <summary>
    /// Arrow keys while composing a formula poke at cells (Excel "point" mode). Returns true when the
    /// key was consumed as a poke; false lets the editor move the caret normally.
    /// </summary>
    private bool TryPointModeArrow(Key key, bool shift)
    {
        if (!_editing)
        {
            return false;
        }

        string text = _editor.Text ?? string.Empty;
        if (text.Length == 0 || text[0] != '=' || !ArrowDelta(key, out int dRow, out int dColumn))
        {
            return false;
        }

        int caret = _editor.CaretIndex;
        // Once keyboard point mode is live we own the inserted span and keep poking; typing resets it
        // (via ResetPointMode on a non-programmatic text change). Don't gate on the editor's reported
        // caret — a programmatic Text set can move it out from under us.
        bool continuing = _pointKeyboard && _pointInsertStart >= 0;
        if (!continuing)
        {
            // Start a fresh poke only at the end of the text, where a reference is expected (after
            // an operator / '=' ). Otherwise the arrows are editing existing text — move the caret.
            if (caret != text.Length || !IsReferenceExpected(text, caret))
            {
                return false;
            }

            _pointAnchorRow = _pointRow = Selection.ActiveRow;
            _pointAnchorColumn = _pointColumn = Selection.ActiveColumn;
            _pointInsertStart = -1;
            _pointKeyboard = true;
        }

        _pointRow = Math.Clamp(_pointRow + dRow, 0, SheetDimensions.MaxRows - 1);
        _pointColumn = Math.Clamp(_pointColumn + dColumn, 0, SheetDimensions.MaxColumns - 1);
        if (!shift)
        {
            _pointAnchorRow = _pointRow;
            _pointAnchorColumn = _pointColumn;
        }

        ApplyPointReference(PointReferenceText(), caret);
        SetPointTarget(
            Math.Min(_pointAnchorRow, _pointRow), Math.Min(_pointAnchorColumn, _pointColumn),
            Math.Max(_pointAnchorRow, _pointRow), Math.Max(_pointAnchorColumn, _pointColumn));
        EnsureVisible(_pointRow, _pointColumn);
        Redraw();
        return true;
    }

    private static bool ArrowDelta(Key key, out int dRow, out int dColumn)
    {
        (dRow, dColumn) = key switch
        {
            Key.Up => (-1, 0),
            Key.Down => (1, 0),
            Key.Left => (0, -1),
            Key.Right => (0, 1),
            _ => (0, 0),
        };

        return dRow != 0 || dColumn != 0;
    }

    /// <summary>The reference text for the current pointer: a single cell, or a normalized range when extended with Shift.</summary>
    private string PointReferenceText() =>
        RangeReferenceText(_pointAnchorRow, _pointAnchorColumn, _pointRow, _pointColumn);

    /// <summary>Replace the previously poked reference (if it still abuts the caret) or insert a new one at the caret.</summary>
    private void ApplyPointReference(string reference, int caret)
    {
        string text = _editor.Text ?? string.Empty;
        int start;
        if (_pointInsertStart >= 0 && _pointInsertStart + _pointInsertLength <= text.Length)
        {
            start = _pointInsertStart;
            SetEditorText(text.Remove(start, _pointInsertLength).Insert(start, reference));
        }
        else
        {
            start = Math.Min(caret, text.Length);
            SetEditorText(text.Insert(start, reference));
        }

        _pointInsertStart = start;
        _pointInsertLength = reference.Length;
        _editor.CaretIndex = start + reference.Length;
    }

    /// <summary>True when the inline editor currently holds a formula (its text starts with '=').</summary>
    private bool IsFormulaEdit() => (_editor.Text ?? string.Empty).StartsWith('=');

    /// <summary>
    /// Tunnel handler: a left-press on a cell while composing a formula inserts that cell's reference
    /// (and a drag from there extends it to a range), instead of committing the edit. Handled here so
    /// it runs before the focus class handler — moving focus to the grid would blur and commit the
    /// editor first. <see cref="_suppressBlurCommit"/> guards against any blur that still slips through.
    /// </summary>
    private void PointModePreviewPressed(object? sender, PointerPressedEventArgs e)
    {
        // The blur-commit was deferred to us (see the editor's LostFocus); _editing is still true.
        if (!_editing || !IsFormulaEdit() || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (IsOverScrollBar(point))
        {
            return; // a click on an overlay scroll bar is for scrolling, not a point-mode reference
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool onCell = HitTest(point, out _, out _, out bool rowHeader, out bool columnHeader)
            && !rowHeader && !columnHeader;
        if (onCell && TryPointModeInsert(point, shift))
        {
            // Reference inserted; arm a drag so the user can keep extending it into a range from the
            // same anchor (TryPointModeInsert set _pointDragAnchor), and keep editing.
            _pointDragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // A header press, or a press where a reference isn't expected: this is a real commit. Finish
        // the edit now (LostFocus deferred it) so the press falls through to normal selection.
        CommitEdit(moveRow: 0, moveColumn: 0, refocus: false);
    }

    private bool TryPointModeInsert(Point point, bool shift)
    {
        string text = _editor.Text ?? string.Empty;
        if (text.Length == 0 || text[0] != '=')
        {
            return false; // only when editing a formula
        }

        if (!HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader) ||
            rowHeader || columnHeader)
        {
            return false; // only clicks on cells
        }

        int caret = _editor.CaretIndex;

        // Shift+click extends the just-poked reference into a range from its anchor to the clicked cell,
        // the way Shift+click extends a normal grid selection. Needs a live anchor and an inserted span.
        bool extend = shift && _pointHasClickAnchor && _pointInsertStart >= 0
            && _pointInsertStart + _pointInsertLength <= text.Length;
        int anchorRow = extend ? _pointClickAnchorRow : row;
        int anchorColumn = extend ? _pointClickAnchorColumn : column;
        string reference = RangeReferenceText(anchorRow, anchorColumn, row, column);

        int insertedAt;
        if (extend || (_pointInsertStart >= 0 && _pointInsertStart + _pointInsertLength == caret))
        {
            // Replace the reference inserted by the previous click (a Shift+click turns it into a range).
            insertedAt = _pointInsertStart;
            SetEditorText(text.Remove(_pointInsertStart, _pointInsertLength).Insert(_pointInsertStart, reference));
        }
        else if (IsReferenceExpected(text, caret))
        {
            insertedAt = caret;
            SetEditorText(text.Insert(caret, reference));
        }
        else
        {
            return false; // not an operand position — let the caller commit the edit
        }

        _pointInsertStart = insertedAt;
        _pointInsertLength = reference.Length;
        _editor.CaretIndex = insertedAt + reference.Length;

        // A plain click moves the anchor to the clicked cell; a Shift+click keeps the existing anchor so
        // further Shift+clicks re-extend from it. The drag anchor tracks the same corner so a drag begun
        // now continues the range from the anchor, not the cell just pressed.
        if (!extend)
        {
            _pointClickAnchorRow = row;
            _pointClickAnchorColumn = column;
            _pointHasClickAnchor = true;
        }

        _pointDragAnchorRow = anchorRow;
        _pointDragAnchorColumn = anchorColumn;
        SetPointTarget(
            Math.Min(anchorRow, row), Math.Min(anchorColumn, column),
            Math.Max(anchorRow, row), Math.Max(anchorColumn, column));
        _editor.Focus();
        Redraw();
        return true;
    }

    /// <summary>
    /// Drag-extend the reference inserted by the press into a range spanning the press-anchor cell to
    /// the cell now under the pointer (task 1). A single cell collapses back to a plain reference.
    /// </summary>
    private void UpdatePointDrag(Point point)
    {
        if (_pointInsertStart < 0 || !HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader)
            || rowHeader || columnHeader)
        {
            return;
        }

        string reference = RangeReferenceText(_pointDragAnchorRow, _pointDragAnchorColumn, row, column);

        string text = _editor.Text ?? string.Empty;
        if (_pointInsertStart + _pointInsertLength > text.Length)
        {
            return; // the inserted span no longer fits the text (defensive); leave it untouched
        }

        SetEditorText(text.Remove(_pointInsertStart, _pointInsertLength).Insert(_pointInsertStart, reference));
        _pointInsertLength = reference.Length;
        _editor.CaretIndex = _pointInsertStart + reference.Length;
        SetPointTarget(
            Math.Min(_pointDragAnchorRow, row), Math.Min(_pointDragAnchorColumn, column),
            Math.Max(_pointDragAnchorRow, row), Math.Max(_pointDragAnchorColumn, column));
        EnsureVisible(row, column);
        Redraw();
    }

    private void SetEditorText(string text)
    {
        _programmaticText = text;
        _editor.Text = text;
    }

    /// <summary>True when the caret sits where a reference/operand is expected (after an operator).</summary>
    private static bool IsReferenceExpected(string text, int caret)
    {
        int i = System.Math.Min(caret, text.Length) - 1;
        while (i >= 0 && text[i] == ' ')
        {
            i--;
        }

        if (i < 0)
        {
            return false;
        }

        return text[i] is '=' or '+' or '-' or '*' or '/' or '^' or '(' or ',' or ':' or '<' or '>' or '&' or '%';
    }
}
