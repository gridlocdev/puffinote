using System.Diagnostics.CodeAnalysis;

namespace Cellestra.Core.Model;

/// <summary>
/// A cell location in A1 notation. Carries zero-based <see cref="Column"/>/<see cref="Row"/>,
/// remembers <b>absolute vs relative</b> per axis (<c>$A$1</c> vs <c>A1</c>), and may be
/// <b>sheet-qualified</b> (<c>Sheet2.A1</c>). Two addresses are equal only when every part
/// matches; compare <see cref="Position"/> to ask "same cell regardless of $ or sheet".
/// </summary>
public readonly record struct CellAddress
{
    public CellAddress(int column, int row, bool columnAbsolute = false, bool rowAbsolute = false, string? sheet = null)
    {
        if (!SheetDimensions.IsValidColumn(column))
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Column index out of range.");
        }

        if (!SheetDimensions.IsValidRow(row))
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "Row index out of range.");
        }

        Column = column;
        Row = row;
        ColumnAbsolute = columnAbsolute;
        RowAbsolute = rowAbsolute;
        Sheet = sheet;
    }

    /// <summary>Zero-based column index (A = 0).</summary>
    public int Column { get; }

    /// <summary>Zero-based row index (row 1 = 0).</summary>
    public int Row { get; }

    public bool ColumnAbsolute { get; }

    public bool RowAbsolute { get; }

    /// <summary>The sheet name when the reference is qualified (e.g. <c>Sheet2.A1</c>); otherwise null.</summary>
    public string? Sheet { get; }

    /// <summary>The bare (row, column) position — identity ignoring $ flags and sheet.</summary>
    public (int Row, int Column) Position => (Row, Column);

    public string ColumnLabel => SheetDimensions.ColumnToLetters(Column);

    /// <summary>One-based row label as shown in the UI.</summary>
    public int RowLabel => Row + 1;

    public CellAddress WithSheet(string? sheet) => new(Column, Row, ColumnAbsolute, RowAbsolute, sheet);

    public CellAddress WithoutSheet() => new(Column, Row, ColumnAbsolute, RowAbsolute, null);

    /// <summary>Strip absolute flags and sheet, leaving a plain relative position.</summary>
    public CellAddress Relative() => new(Column, Row);

    /// <summary>Offset the relative parts of this address; absolute parts stay put (ReferenceAdjuster semantics).</summary>
    public CellAddress Shift(int columnDelta, int rowDelta)
    {
        int newColumn = ColumnAbsolute ? Column : Column + columnDelta;
        int newRow = RowAbsolute ? Row : Row + rowDelta;
        return new CellAddress(newColumn, newRow, ColumnAbsolute, RowAbsolute, Sheet);
    }

    public override string ToString()
    {
        string a1 = $"{(ColumnAbsolute ? "$" : string.Empty)}{ColumnLabel}{(RowAbsolute ? "$" : string.Empty)}{RowLabel}";
        return Sheet is null ? a1 : $"{QuoteSheetIfNeeded(Sheet)}.{a1}";
    }

    /// <summary>Parse an A1 reference, optionally sheet-qualified (<c>Sheet1.B7</c> or <c>'My Sheet'.B7</c>).</summary>
    public static CellAddress Parse(string text)
    {
        if (!TryParse(text, out var address))
        {
            throw new FormatException($"'{text}' is not a valid cell reference.");
        }

        return address;
    }

    public static bool TryParse(string? text, out CellAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        string? sheet = null;

        // Split an optional "Sheet." (or 'Quoted Sheet'.) prefix at the LAST unquoted dot.
        int dot = FindSheetSeparator(span);
        if (dot >= 0)
        {
            ReadOnlySpan<char> sheetPart = span[..dot];
            if (!TryUnquoteSheet(sheetPart, out sheet))
            {
                return false;
            }

            span = span[(dot + 1)..];
        }

        return TryParseA1(span, sheet, out address);
    }

    private static bool TryParseA1(ReadOnlySpan<char> span, string? sheet, out CellAddress address)
    {
        address = default;
        if (span.IsEmpty)
        {
            return false;
        }

        int i = 0;
        bool colAbs = false;
        if (span[i] == '$')
        {
            colAbs = true;
            i++;
        }

        int letterStart = i;
        while (i < span.Length && char.IsAsciiLetter(span[i]))
        {
            i++;
        }

        if (i == letterStart)
        {
            return false;
        }

        ReadOnlySpan<char> letters = span[letterStart..i];

        bool rowAbs = false;
        if (i < span.Length && span[i] == '$')
        {
            rowAbs = true;
            i++;
        }

        int digitStart = i;
        while (i < span.Length && char.IsAsciiDigit(span[i]))
        {
            i++;
        }

        if (i == digitStart || i != span.Length)
        {
            return false;
        }

        if (!SheetDimensions.TryLettersToColumn(letters, out int column))
        {
            return false;
        }

        if (!int.TryParse(span[digitStart..i], out int oneBasedRow) || oneBasedRow < 1)
        {
            return false;
        }

        int row = oneBasedRow - 1;
        if (!SheetDimensions.IsValidRow(row))
        {
            return false;
        }

        address = new CellAddress(column, row, colAbs, rowAbs, sheet);
        return true;
    }

    private static int FindSheetSeparator(ReadOnlySpan<char> span)
    {
        bool inQuote = false;
        int lastDot = -1;
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (c == '\'')
            {
                inQuote = !inQuote;
            }
            else if (c == '.' && !inQuote)
            {
                lastDot = i;
            }
        }

        return lastDot;
    }

    private static bool TryUnquoteSheet(ReadOnlySpan<char> sheet, [NotNullWhen(true)] out string? name)
    {
        name = null;
        if (sheet.IsEmpty)
        {
            return false;
        }

        if (sheet[0] == '\'' && sheet[^1] == '\'' && sheet.Length >= 2)
        {
            // ODF quotes embedded apostrophes by doubling them.
            name = sheet[1..^1].ToString().Replace("''", "'", StringComparison.Ordinal);
            return name.Length > 0;
        }

        name = sheet.ToString();
        return true;
    }

    private static string QuoteSheetIfNeeded(string sheet)
    {
        bool needsQuotes = sheet.Length == 0;
        foreach (char c in sheet)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                needsQuotes = true;
                break;
            }
        }

        return needsQuotes ? $"'{sheet.Replace("'", "''", StringComparison.Ordinal)}'" : sheet;
    }
}
