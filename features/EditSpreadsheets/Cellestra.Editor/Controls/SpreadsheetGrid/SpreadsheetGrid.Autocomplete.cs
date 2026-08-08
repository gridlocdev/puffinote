using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Type-ahead autocomplete (#20): while typing plain text into a cell, the nearest matching text
/// entry higher in the same column is offered — its remaining suffix is appended and selected, so
/// the next keystroke replaces it and Enter accepts it. Only triggers while appending at the end of
/// non-formula text, so backspacing or editing mid-string never fights the user.
///
/// <para>Matching is <b>case-sensitive</b>, which is what makes the feature stay out of the way. Case
/// is the one thing the typist is unambiguously asserting: type "ID" under a column of "Id" and an
/// insensitive match would silently rewrite the two characters you just chose. Diverging in case
/// simply ends the offer for that entry, and what you typed stands.</para>
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

    /// <summary>
    /// The nearest text entry above <paramref name="activeRow"/> in this column that starts with
    /// <paramref name="prefix"/>, or null.
    ///
    /// <para>Walks up from the cell being typed in and stops at the first gap — the contiguous block the
    /// cell belongs to is the list it is an entry of, which is both the useful answer and a bounded one.
    /// Scanning the whole sheet's cell store on every keystroke, which is what this did before, is what
    /// made typing into a large workbook feel like it was lagging a character behind.</para>
    ///
    /// <para>A table's header row ends the walk without being offered: the header is the column's
    /// <i>title</i>, not one of its entries, so completing "F" to "Fruit" while you type the first fruit
    /// is offering the label of the list rather than a member of it. Anything above the header is
    /// outside the table, so it is a different list either way.</para>
    /// </summary>
    private static string? FindColumnCompletion(Worksheet sheet, int column, int activeRow, string prefix)
    {
        for (int row = activeRow - 1; row >= 0; row--)
        {
            if (sheet.FindTable(row, column) is { HasHeader: true } table && row == table.Range.Top)
            {
                return null; // the title of the column, not a value in it
            }

            var cell = sheet.Find(row, column);
            if (cell is null || cell.Value.IsEmpty)
            {
                return null; // the block ended; entries beyond it belong to a different list
            }

            if (!cell.Value.IsText)
            {
                continue;
            }

            string value = cell.Value.AsText;
            if (value.Length > prefix.Length && value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return value; // nearest first — the walk is already in order
            }
        }

        return null;
    }
}
