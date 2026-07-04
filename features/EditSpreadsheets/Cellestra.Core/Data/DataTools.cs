using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Csv;
using Cellestra.Core.Values;

namespace Cellestra.Core.Data;

/// <summary>
/// Removes duplicate rows from a range, keeping the first occurrence (PRD §8.14). Rows are
/// compared across the chosen key columns; survivors are compacted to the top and the trailing
/// rows cleared. Returns how many rows were removed.
/// </summary>
public static class DuplicateRemover
{
    public static int Remove(Worksheet sheet, CellRange range, IReadOnlyList<int> keyColumns, bool hasHeader)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        int firstDataRow = range.Top + (hasHeader ? 1 : 0);
        if (firstDataRow > range.Bottom)
        {
            return 0;
        }

        var keys = keyColumns.Count > 0
            ? keyColumns
            : [.. Enumerable.Range(range.Left, range.ColumnCount)];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var survivors = new List<Cell?[]>();
        int removed = 0;

        for (int row = firstDataRow; row <= range.Bottom; row++)
        {
            string key = string.Join("\u001f", keys.Select(c => sheet.GetValue(row, c).ToInvariantString()));
            if (!seen.Add(key))
            {
                removed++;
                continue;
            }

            var cells = new Cell?[range.ColumnCount];
            for (int c = 0; c < range.ColumnCount; c++)
            {
                cells[c] = sheet.Find(row, range.Left + c)?.Clone();
            }

            survivors.Add(cells);
        }

        // Rewrite survivors at the top, clear the rest of the data range.
        int writeRow = firstDataRow;
        foreach (var cells in survivors)
        {
            for (int c = 0; c < range.ColumnCount; c++)
            {
                sheet.PutCell(writeRow, range.Left + c, cells[c]);
            }

            writeRow++;
        }

        for (; writeRow <= range.Bottom; writeRow++)
        {
            for (int c = 0; c < range.ColumnCount; c++)
            {
                sheet.Remove(writeRow, range.Left + c);
            }
        }

        return removed;
    }
}

/// <summary>
/// Splits delimited text in a single column across multiple columns to the right (PRD §8.14),
/// using the same quoting rules as CSV import (§8.4). Overwrites cells to the right.
/// </summary>
public static class TextToColumns
{
    public static int Split(Worksheet sheet, CellRange sourceColumn, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        int column = sourceColumn.Left;
        int maxColumns = 1;
        var options = new CsvOptions { Delimiter = delimiter };

        for (int row = sourceColumn.Top; row <= sourceColumn.Bottom; row++)
        {
            var value = sheet.GetValue(row, column);
            if (!value.IsText)
            {
                continue;
            }

            var rows = CsvReader.Parse(value.AsText, options);
            var fields = rows.Count > 0 ? rows[0] : [];
            maxColumns = Math.Max(maxColumns, fields.Count);

            for (int i = 0; i < fields.Count; i++)
            {
                sheet.SetValue(row, column + i, fields[i].Length == 0 ? CellValue.Empty : CellValue.Text(fields[i]));
            }
        }

        return maxColumns;
    }
}
