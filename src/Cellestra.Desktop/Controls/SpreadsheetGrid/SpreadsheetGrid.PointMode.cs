using Avalonia;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Point mode (#17): while typing a formula, clicking another cell inserts that cell's reference
/// into the formula at the caret instead of moving the selection. Consecutive clicks (with no
/// typing in between) replace the just-inserted reference, so you can click around to adjust it;
/// type an operator (or <c>:</c>) and the next click appends a new reference — letting you point at
/// a range as <c>A1:B2</c>.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private int _pointInsertStart = -1; // start of the last click-inserted reference, or -1
    private int _pointInsertLength;
    private bool _programmaticEdit;      // suppresses the type-reset while we edit the text ourselves

    private void ResetPointMode() => _pointInsertStart = -1;

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
        _programmaticEdit = true;
        _editor.Text = text;
        _programmaticEdit = false;
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
