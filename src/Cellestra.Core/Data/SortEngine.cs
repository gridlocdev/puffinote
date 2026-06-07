using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Data;

/// <summary>One level of a multi-level sort: a column and a direction (PRD §8.11).</summary>
public sealed record SortLevel(int Column, bool Ascending = true);

/// <summary>A stable, multi-level sort specification with an optional header row.</summary>
public sealed record SortSpec(IReadOnlyList<SortLevel> Levels, bool HasHeader = true);

/// <summary>
/// Stable, multi-level range sort (PRD §8.11). Moves each row's cells and styles together;
/// empty cells sort last. Header rows stay in place. (Reference adjustment on sort is a
/// later refinement; v1 moves formula text with the row.)
/// </summary>
public static class SortEngine
{
    public static void Sort(Worksheet sheet, CellRange range, SortSpec spec)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(spec);

        int firstDataRow = range.Top + (spec.HasHeader ? 1 : 0);
        if (firstDataRow > range.Bottom)
        {
            return;
        }

        // Snapshot each data row's cells across the range's columns.
        var rows = new List<RowSnapshot>();
        for (int row = firstDataRow; row <= range.Bottom; row++)
        {
            var cells = new Cell?[range.ColumnCount];
            for (int c = 0; c < range.ColumnCount; c++)
            {
                cells[c] = sheet.Find(row, range.Left + c)?.Clone();
            }

            rows.Add(new RowSnapshot(row, cells));
        }

        var ordered = rows
            .OrderBy(r => r, Comparer<RowSnapshot>.Create((a, b) => Compare(a, b, spec, range.Left)))
            .ToList();

        // Write the rows back in sorted order.
        for (int i = 0; i < ordered.Count; i++)
        {
            int targetRow = firstDataRow + i;
            for (int c = 0; c < range.ColumnCount; c++)
            {
                sheet.PutCell(targetRow, range.Left + c, ordered[i].Cells[c]);
            }
        }
    }

    private static int Compare(RowSnapshot a, RowSnapshot b, SortSpec spec, int rangeLeft)
    {
        foreach (var level in spec.Levels)
        {
            int index = level.Column - rangeLeft;
            if (index < 0 || index >= a.Cells.Length)
            {
                continue;
            }

            var va = a.Cells[index]?.Value ?? CellValue.Empty;
            var vb = b.Cells[index]?.Value ?? CellValue.Empty;

            // Empty cells always sort last, regardless of direction.
            if (va.IsEmpty != vb.IsEmpty)
            {
                return va.IsEmpty ? 1 : -1;
            }

            int cmp = ValueComparer.Compare(va, vb, out _);
            if (cmp != 0)
            {
                return level.Ascending ? cmp : -cmp;
            }
        }

        return 0; // stable: OrderBy preserves original order for equal keys
    }

    private sealed record RowSnapshot(int OriginalRow, Cell?[] Cells);
}
