using System;
using Avalonia;
using Avalonia.Input;
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

    // Keyboard point mode: the cell the arrows are currently pointing at, plus the range anchor.
    private bool _pointKeyboard;
    private int _pointRow, _pointColumn, _pointAnchorRow, _pointAnchorColumn;

    private void ResetPointMode()
    {
        _pointInsertStart = -1;
        _pointKeyboard = false;
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
    private string PointReferenceText()
    {
        int top = Math.Min(_pointAnchorRow, _pointRow), bottom = Math.Max(_pointAnchorRow, _pointRow);
        int left = Math.Min(_pointAnchorColumn, _pointColumn), right = Math.Max(_pointAnchorColumn, _pointColumn);
        string topLeft = new CellAddress(left, top).ToString();
        return top == bottom && left == right
            ? topLeft
            : topLeft + ":" + new CellAddress(right, bottom);
    }

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

    private bool TryPointModeInsert(Point point)
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
        string reference = new CellAddress(column, row).ToString(); // relative A1, no sheet

        int insertedAt;
        if (_pointInsertStart >= 0 && _pointInsertStart + _pointInsertLength == caret)
        {
            // Replace the reference inserted by the previous click.
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
        _editor.Focus();
        Redraw();
        return true;
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
