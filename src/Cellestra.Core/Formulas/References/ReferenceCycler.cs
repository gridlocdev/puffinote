namespace Cellestra.Core.Formulas.References;

/// <summary>
/// Cycles the absolute/relative markers of the cell reference at the caret while editing a
/// formula (the F4 key): <c>A1 → $A$1 → A$1 → $A1 → A1</c>. Pure text manipulation so it works
/// the same in the in-cell editor and the formula bar, and is unit-testable.
/// </summary>
public static class ReferenceCycler
{
    /// <summary>
    /// Returns the formula text with the reference at <paramref name="caret"/> cycled to its next
    /// absolute/relative form, and the caret repositioned to the reference's end. When the caret is
    /// not on a cell reference the text is returned unchanged.
    /// </summary>
    public static (string Text, int Caret) Cycle(string text, int caret)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (text, caret);
        }

        caret = Math.Clamp(caret, 0, text.Length);

        int start = caret;
        while (start > 0 && IsReferenceChar(text[start - 1]))
        {
            start--;
        }

        int end = caret;
        while (end < text.Length && IsReferenceChar(text[end]))
        {
            end++;
        }

        if (end <= start || !TryParseReference(text[start..end], out var reference))
        {
            return (text, caret);
        }

        string cycled = NextForm(reference);
        string updated = text[..start] + cycled + text[end..];
        return (updated, start + cycled.Length);
    }

    private static bool IsReferenceChar(char c) => char.IsLetterOrDigit(c) || c == '$';

    private static bool TryParseReference(string token, out Reference reference)
    {
        reference = default;
        int i = 0;

        bool columnAbsolute = i < token.Length && token[i] == '$';
        if (columnAbsolute)
        {
            i++;
        }

        int columnStart = i;
        while (i < token.Length && char.IsLetter(token[i]))
        {
            i++;
        }

        if (i == columnStart)
        {
            return false; // no column letters
        }

        string column = token[columnStart..i];

        bool rowAbsolute = i < token.Length && token[i] == '$';
        if (rowAbsolute)
        {
            i++;
        }

        int rowStart = i;
        while (i < token.Length && char.IsDigit(token[i]))
        {
            i++;
        }

        if (i == rowStart || i != token.Length)
        {
            return false; // no row digits, or trailing junk (e.g. a function name)
        }

        reference = new Reference(columnAbsolute, column, rowAbsolute, token[rowStart..i]);
        return true;
    }

    private static string NextForm(Reference reference)
    {
        // A1 -> $A$1 -> A$1 -> $A1 -> A1
        var (columnAbsolute, rowAbsolute) = (reference.ColumnAbsolute, reference.RowAbsolute) switch
        {
            (false, false) => (true, true),
            (true, true) => (false, true),
            (false, true) => (true, false),
            _ => (false, false),
        };

        return (columnAbsolute ? "$" : string.Empty) + reference.Column +
               (rowAbsolute ? "$" : string.Empty) + reference.Row;
    }

    private readonly record struct Reference(bool ColumnAbsolute, string Column, bool RowAbsolute, string Row);
}
