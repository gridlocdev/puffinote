using System;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Type-ahead autocomplete (#20): while typing plain text into a cell, the nearest matching text
/// entry higher in the same column is offered — its remaining suffix is appended and selected, so
/// the next keystroke replaces it and Enter accepts it. Only triggers while appending at the end of
/// non-formula text, so backspacing or editing mid-string never fights the user.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private int _lastEditorTextLength;

    private void ResetAutocomplete() => _lastEditorTextLength = _editor.Text?.Length ?? 0;

    private void OfferCompletion()
    {
        string text = _editor.Text ?? string.Empty;
        bool grew = text.Length > _lastEditorTextLength;
        _lastEditorTextLength = text.Length;

        // Only when the user just appended a character to plain text at the caret end.
        if (!grew || text.Length == 0 || text[0] == '=' || _editor.CaretIndex != text.Length)
        {
            return;
        }

        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        string? match = FindColumnCompletion(sheet, Selection.ActiveColumn, Selection.ActiveRow, text);
        if (match is null)
        {
            return;
        }

        // Append the suffix, selected, so typing replaces it and Enter keeps the whole word.
        _programmaticText = match;
        _editor.Text = match;
        _editor.SelectionStart = text.Length;
        _editor.SelectionEnd = match.Length;
        _lastEditorTextLength = text.Length; // the real typed length, so the next keystroke counts as growth
    }

    private static string? FindColumnCompletion(Worksheet sheet, int column, int activeRow, string prefix)
    {
        string? best = null;
        int bestRow = -1;
        foreach (var (row, col, cell) in sheet.EnumerateCells())
        {
            if (col != column || row >= activeRow || !cell.Value.IsText)
            {
                continue;
            }

            string value = cell.Value.AsText;
            if (value.Length <= prefix.Length ||
                !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (row > bestRow)
            {
                bestRow = row; // nearest non-empty text cell above the active cell
                best = value;
            }
        }

        return best;
    }
}
