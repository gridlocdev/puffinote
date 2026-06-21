namespace Cellestra.Core.Model;

/// <summary>
/// A rectangular block of cells, defined by two corner <see cref="CellAddress"/>es.
/// The corners keep their own $ flags and sheet for faithful formula round-tripping;
/// the normalized <see cref="Top"/>/<see cref="Left"/>/<see cref="Bottom"/>/<see cref="Right"/>
/// bounds are always min/max-ordered for iteration and containment.
/// </summary>
public readonly record struct CellRange
{
    public CellRange(CellAddress start, CellAddress end)
    {
        Start = start;
        End = end;
        Top = Math.Min(start.Row, end.Row);
        Bottom = Math.Max(start.Row, end.Row);
        Left = Math.Min(start.Column, end.Column);
        Right = Math.Max(start.Column, end.Column);
    }

    /// <summary>A single-cell range.</summary>
    public CellRange(CellAddress single) : this(single, single)
    {
    }

    public CellAddress Start { get; }

    public CellAddress End { get; }

    public int Top { get; }

    public int Bottom { get; }

    public int Left { get; }

    public int Right { get; }

    public int RowCount => Bottom - Top + 1;

    public int ColumnCount => Right - Left + 1;

    public int CellCount => RowCount * ColumnCount;

    public bool IsSingleCell => RowCount == 1 && ColumnCount == 1;

    /// <summary>The sheet of the start corner (ranges live on a single sheet).</summary>
    public string? Sheet => Start.Sheet;

    public bool Contains(int row, int column) =>
        row >= Top && row <= Bottom && column >= Left && column <= Right;

    public bool Contains(CellAddress address) => Contains(address.Row, address.Column);

    public bool Intersects(CellRange other) =>
        Left <= other.Right && Right >= other.Left && Top <= other.Bottom && Bottom >= other.Top;

    /// <summary>The top-left position as a plain relative address (no $, no sheet).</summary>
    public CellAddress TopLeft => new(Left, Top, sheet: Sheet);

    public CellAddress BottomRight => new(Right, Bottom, sheet: Sheet);

    /// <summary>Enumerate every (row, column) position in the rectangle, row-major.</summary>
    public IEnumerable<(int Row, int Column)> Positions()
    {
        for (int r = Top; r <= Bottom; r++)
        {
            for (int c = Left; c <= Right; c++)
            {
                yield return (r, c);
            }
        }
    }

    public CellRange WithSheet(string? sheet) => new(Start.WithSheet(sheet), End.WithSheet(sheet));

    public override string ToString()
    {
        if (IsSingleCell)
        {
            return Start.ToString();
        }

        // Sheet qualifier (if any) belongs on the whole range, not repeated on the end corner.
        string startText = Start.ToString();
        string endText = End.WithoutSheet().ToString();
        return $"{startText}:{endText}";
    }

    public static CellRange Parse(string text)
    {
        if (!TryParse(text, out var range))
        {
            throw new FormatException($"'{text}' is not a valid range reference.");
        }

        return range;
    }

    public static bool TryParse(string? text, out CellRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            if (!CellAddress.TryParse(text, out var single))
            {
                return false;
            }

            range = new CellRange(single);
            return true;
        }

        string startText = text[..colon];
        string endText = text[(colon + 1)..];
        if (!CellAddress.TryParse(startText, out var start))
        {
            return false;
        }

        if (!CellAddress.TryParse(endText, out var end))
        {
            return false;
        }

        // A range lives on a single sheet, so the sheet qualifier belongs on the start corner. A
        // qualifier on the end corner is only valid when it matches the start's; a different (or
        // lone end-corner) sheet — e.g. "Sheet1.A1:Sheet2.B2" — is malformed and rejected rather
        // than silently kept as a contradictory cross-sheet range.
        if (end.Sheet is not null && !string.Equals(end.Sheet, start.Sheet, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (start.Sheet is not null && end.Sheet is null)
        {
            end = end.WithSheet(start.Sheet);
        }

        range = new CellRange(start, end);
        return true;
    }
}
