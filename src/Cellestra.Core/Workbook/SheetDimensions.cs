using System.Text;

namespace Cellestra.Core.Model;

/// <summary>
/// Sheet size limits and the A1 ↔ index conversions for column letters.
/// Bounds match LibreOffice Calc so files round-trip without clamping.
/// All indices in the model are <b>zero-based</b> (column A = 0, row 1 = 0);
/// A1 notation is one-based for rows and letter-based for columns.
/// </summary>
public static class SheetDimensions
{
    /// <summary>Maximum columns (A … XFD), matching Calc.</summary>
    public const int MaxColumns = 16384;

    /// <summary>Maximum rows, matching Calc.</summary>
    public const int MaxRows = 1048576;

    public const int MinIndex = 0;

    public static bool IsValidColumn(int column) => column is >= MinIndex and < MaxColumns;

    public static bool IsValidRow(int row) => row is >= MinIndex and < MaxRows;

    /// <summary>Convert a zero-based column index to its letter label (0 → "A", 26 → "AA").</summary>
    public static string ColumnToLetters(int column)
    {
        if (!IsValidColumn(column))
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Column index out of range.");
        }

        // Bijective base-26 (no zero digit), so we offset by 1 each step.
        var sb = new StringBuilder(3);
        int n = column;
        do
        {
            sb.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return sb.ToString();
    }

    /// <summary>Convert a column letter label to its zero-based index ("A" → 0). Case-insensitive.</summary>
    public static int LettersToColumn(ReadOnlySpan<char> letters)
    {
        if (letters.IsEmpty)
        {
            throw new FormatException("Empty column label.");
        }

        long result = 0;
        foreach (char raw in letters)
        {
            char c = char.ToUpperInvariant(raw);
            if (c is < 'A' or > 'Z')
            {
                throw new FormatException($"Invalid column label character '{raw}'.");
            }

            result = (result * 26) + (c - 'A' + 1);
            if (result > MaxColumns)
            {
                throw new FormatException("Column label out of range.");
            }
        }

        return (int)(result - 1);
    }

    public static bool TryLettersToColumn(ReadOnlySpan<char> letters, out int column)
    {
        try
        {
            column = LettersToColumn(letters);
            return true;
        }
        catch (FormatException)
        {
            column = -1;
            return false;
        }
    }
}
