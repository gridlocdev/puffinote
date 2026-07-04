using Cellestra.Core.Model;

namespace Cellestra.Core.Data;

/// <summary>
/// A named, auto-expanding table over a rectangular range (PRD §8.10). Persisted as an ODF
/// database range (<c>table:database-range</c>) — never an Excel ListObject — so formulas use
/// ordinary A1/named references, not <c>Table[Column]</c>. Carries its own filter/sort state.
/// </summary>
public sealed record Table(string Name, CellRange Range, bool HasHeader = true)
{
    public FilterSpec? Filter { get; init; }

    public SortSpec? Sort { get; init; }

    /// <summary>
    /// When set, the table's first column is styled like the header row — bold, on the header fill —
    /// to read as a row-label header (task 5). The styling lives on the cells (so it round-trips as
    /// ordinary cell formatting); this flag tracks the designation for toggling and banding.
    /// </summary>
    public bool HasHeaderColumn { get; init; }

    /// <summary>The header row's column labels, when the table has a header.</summary>
    public IReadOnlyList<string> HeaderLabels(Worksheet sheet)
    {
        if (!HasHeader)
        {
            return [.. Enumerable.Range(1, Range.ColumnCount).Select(i => $"Column{i}")];
        }

        var labels = new List<string>(Range.ColumnCount);
        for (int c = 0; c < Range.ColumnCount; c++)
        {
            var value = sheet.GetValue(Range.Top, Range.Left + c);
            labels.Add(value.IsText ? value.AsText : $"Column{c + 1}");
        }

        return labels;
    }

    public Table WithRange(CellRange range) => this with { Range = range };
}

/// <summary>
/// Auto-expand rules for a table (PRD §8.10): typing in the row directly below or the column
/// directly to the right absorbs that line; deleting at the edge shrinks it.
/// </summary>
public static class TableAutoExpand
{
    /// <summary>Return the table's new range after an edit at (row, column), or null if unchanged.</summary>
    public static CellRange? ExpandFor(Table table, int row, int column)
    {
        var range = table.Range;

        bool inColumns = column >= range.Left && column <= range.Right;
        bool inRows = row >= range.Top && row <= range.Bottom;

        // A value typed in the row directly below, within the table's columns → grow down.
        if (row == range.Bottom + 1 && inColumns)
        {
            return new CellRange(range.TopLeft, new CellAddress(range.Right, row));
        }

        // A value typed in the column directly to the right, within the table's rows → grow right.
        if (column == range.Right + 1 && inRows)
        {
            return new CellRange(range.TopLeft, new CellAddress(column, range.Bottom));
        }

        return null;
    }
}
