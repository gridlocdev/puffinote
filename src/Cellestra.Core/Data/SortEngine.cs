using System.Globalization;
using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Data;

/// <summary>One level of a multi-level sort: a column and a direction (PRD §8.11).</summary>
public sealed record SortLevel(int Column, bool Ascending = true);

/// <summary>A stable, multi-level sort specification with an optional header row.</summary>
public sealed record SortSpec(IReadOnlyList<SortLevel> Levels, bool HasHeader = true);

/// <summary>
/// Stable, multi-level range sort (PRD §8.11). Moves each row's cells and styles together,
/// adjusting in-range relative formula references for the move (via <c>ReferenceAdjuster</c>);
/// empty cells sort last and header rows stay in place.
/// </summary>
public static class SortEngine
{
    public static void Sort(Worksheet sheet, CellRange range, SortSpec spec, CultureInfo? culture = null)
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

        // Write the rows back in sorted order, adjusting formula references for the move.
        for (int i = 0; i < ordered.Count; i++)
        {
            int targetRow = firstDataRow + i;
            int rowDelta = targetRow - ordered[i].OriginalRow;
            for (int c = 0; c < range.ColumnCount; c++)
            {
                var cell = ordered[i].Cells[c];
                if (cell is { IsFormula: true } && rowDelta != 0)
                {
                    AdjustFormula(cell, rowDelta, firstDataRow, range.Bottom);
                }

                sheet.PutCell(targetRow, range.Left + c, cell);
            }
        }

        ReapplyActiveFilter(sheet, range, culture);
    }

    /// <summary>
    /// After a sort moves data, any active AutoFilter's per-row Hidden flags are stale — they were
    /// pinned to row indices while the values underneath them moved (bug: wrong rows stay hidden).
    /// Re-run the filter so visibility follows the data. Covers both the sheet-level AutoFilter and
    /// a table whose range overlaps the sorted region. No-op when nothing relevant is filtered.
    /// </summary>
    private static void ReapplyActiveFilter(Worksheet sheet, CellRange sorted, CultureInfo? culture)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (sheet.ActiveFilterSpec is { } sheetSpec &&
            sheet.ActiveFilter is { } filterRange &&
            filterRange.Intersects(sorted))
        {
            FilterEngine.Apply(sheet, filterRange, sheetSpec, culture);
        }

        foreach (var table in sheet.Tables)
        {
            if (table.Filter is { } tableSpec && table.Range.Intersects(sorted))
            {
                FilterEngine.Apply(sheet, table.Range, tableSpec, culture);
            }
        }
    }

    private static void AdjustFormula(Cell cell, int rowDelta, int dataTop, int dataBottom)
    {
        if (Parser.TryParse(cell.Formula!, out var expr, out _) && expr is not null)
        {
            cell.Formula = FormulaWriter.Write(ReferenceAdjuster.ForSortMove(expr, rowDelta, dataTop, dataBottom));
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
