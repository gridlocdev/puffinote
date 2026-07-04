using Cellestra.Core.Model;

namespace Cellestra.Core.Data;

/// <summary>
/// Excel-style "jump to edge" navigation (⌘/Ctrl+Arrow): from the active cell, find the next
/// significant stop in a direction. The rules mirror Excel/Calc:
/// <list type="bullet">
/// <item>From a filled cell whose neighbour is also filled → the last filled cell before a gap.</item>
/// <item>From a filled cell whose neighbour is empty → the next filled cell across the gap.</item>
/// <item>From an empty cell → the next filled cell in that direction.</item>
/// <item>When no more data lies ahead → the last row/column of the used range, or the sheet edge.</item>
/// </list>
/// Pure geometry over <see cref="Worksheet"/>; no selection side effects.
/// </summary>
public static class EdgeNavigator
{
    /// <summary>
    /// The cell ⌘+Arrow should land on from (<paramref name="row"/>, <paramref name="column"/>),
    /// stepping by (<paramref name="rowDelta"/>, <paramref name="columnDelta"/>) — exactly one of which
    /// is non-zero (±1). The result is clamped to the sheet bounds.
    /// </summary>
    public static (int Row, int Column) FindEdge(Worksheet sheet, int row, int column, int rowDelta, int columnDelta)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        int maxRow = SheetDimensions.MaxRows - 1;
        int maxColumn = SheetDimensions.MaxColumns - 1;
        int boundaryRow = rowDelta > 0 ? maxRow : 0;
        int boundaryColumn = columnDelta > 0 ? maxColumn : 0;
        int boundary = rowDelta != 0 ? boundaryRow : boundaryColumn;

        bool Filled(int r, int c) => sheet.Find(r, c) is not null;
        int Axis(int r, int c) => rowDelta != 0 ? r : c;
        bool AtBoundary(int r, int c) => Axis(r, c) == boundary;

        // Already at the sheet edge: nowhere to go.
        if (AtBoundary(row, column))
        {
            return (row, column);
        }

        bool here = Filled(row, column);
        bool next = Filled(row + rowDelta, column + columnDelta);

        int r2 = row, c2 = column;
        void Step()
        {
            r2 += rowDelta;
            c2 += columnDelta;
        }

        if (here && next)
        {
            // Run of data: stop on the last filled cell before the first gap (or the edge).
            while (!AtBoundary(r2, c2) && Filled(r2 + rowDelta, c2 + columnDelta))
            {
                Step();
            }

            return (r2, c2);
        }

        // From an empty cell, or a filled cell facing a gap: skip blanks, then land on the next
        // filled cell. If none is found, stop at the used-range extent (or the sheet edge).
        Step();
        while (!AtBoundary(r2, c2) && !Filled(r2, c2))
        {
            Step();
        }

        if (Filled(r2, c2))
        {
            return (r2, c2);
        }

        // Ran off the data without finding a filled cell: prefer the used-range edge in this
        // direction so a lone ⌘+Down on a sparse sheet lands sensibly rather than at row 1,048,576.
        if (sheet.GetUsedRange() is { } used)
        {
            int edge = rowDelta > 0 ? Math.Max(row, used.Bottom)
                : rowDelta < 0 ? Math.Min(row, used.Top)
                : columnDelta > 0 ? Math.Max(column, used.Right)
                : Math.Min(column, used.Left);
            if (Axis(r2, c2) != edge && IsBetween(Axis(row, column), edge, boundary))
            {
                return rowDelta != 0 ? (edge, column) : (row, edge);
            }
        }

        return (r2, c2);
    }

    // True when target lies strictly past origin on the way to boundary (so we only redirect to a
    // used-range edge that is actually ahead of the starting cell).
    private static bool IsBetween(int origin, int target, int boundary) =>
        boundary >= origin ? target > origin && target <= boundary : target < origin && target >= boundary;
}
