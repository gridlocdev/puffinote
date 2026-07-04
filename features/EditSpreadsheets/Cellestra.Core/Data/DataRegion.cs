using Cellestra.Core.Model;

namespace Cellestra.Core.Data;

/// <summary>
/// Finds the contiguous block of data around a cell (Excel's "current region") — the rectangle
/// reached by expanding from the anchor while adjacent rows/columns still contain data. Used by
/// Sort/Filter/Table when the user has only one cell selected.
/// </summary>
public static class DataRegion
{
    public static CellRange CurrentRegion(Worksheet sheet, int row, int column)
    {
        int top = row, bottom = row, left = column, right = column;
        if (sheet.Find(row, column) is null)
        {
            return new CellRange(new CellAddress(column, row));
        }

        bool grew = true;
        while (grew)
        {
            grew = false;
            if (RowHasData(sheet, top - 1, left, right))
            {
                top--;
                grew = true;
            }
            if (RowHasData(sheet, bottom + 1, left, right))
            {
                bottom++;
                grew = true;
            }
            if (ColumnHasData(sheet, left - 1, top, bottom))
            {
                left--;
                grew = true;
            }
            if (ColumnHasData(sheet, right + 1, top, bottom))
            {
                right++;
                grew = true;
            }
        }

        return new CellRange(new CellAddress(left, top), new CellAddress(right, bottom));
    }

    /// <summary>Guess whether a range's first row is a header: all-text header over rows that include non-text.</summary>
    public static bool LooksLikeHeader(Worksheet sheet, CellRange range)
    {
        if (range.RowCount < 2)
        {
            return false;
        }

        bool headerAllText = true;
        bool bodyHasNonText = false;
        for (int c = range.Left; c <= range.Right; c++)
        {
            var header = sheet.GetValue(range.Top, c);
            if (!header.IsEmpty && !header.IsText)
            {
                headerAllText = false;
            }

            for (int r = range.Top + 1; r <= range.Bottom; r++)
            {
                var value = sheet.GetValue(r, c);
                if (!value.IsEmpty && !value.IsText)
                {
                    bodyHasNonText = true;
                }
            }
        }

        return headerAllText && bodyHasNonText;
    }

    private static bool RowHasData(Worksheet sheet, int row, int left, int right)
    {
        if (row < 0)
        {
            return false;
        }

        for (int c = left; c <= right; c++)
        {
            if (sheet.Find(row, c) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ColumnHasData(Worksheet sheet, int column, int top, int bottom)
    {
        if (column < 0)
        {
            return false;
        }

        for (int r = top; r <= bottom; r++)
        {
            if (sheet.Find(r, column) is not null)
            {
                return true;
            }
        }

        return false;
    }
}
