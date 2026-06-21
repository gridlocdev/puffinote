using System.Globalization;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Data;

/// <summary>
/// One column's AutoFilter criteria: a set of allowed values (by-value checklist) and/or a
/// condition string (e.g. <c>"&gt;5"</c>, <c>"*text*"</c>) evaluated like SUMIF criteria.
/// </summary>
public sealed record ColumnFilter
{
    public IReadOnlySet<string>? AllowedValues { get; init; }

    public string? Condition { get; init; }

    public bool Matches(CellValue value, CultureInfo culture)
    {
        if (AllowedValues is not null && !AllowedValues.Contains(value.ToInvariantString()))
        {
            return false;
        }

        if (Condition is not null && !CriteriaMatcher.Build(CellValue.Text(Condition), culture).Matches(value))
        {
            return false;
        }

        return true;
    }
}

/// <summary>An AutoFilter over a range: per-column criteria plus whether the range has a header.</summary>
public sealed record FilterSpec(IReadOnlyDictionary<int, ColumnFilter> Columns, bool HasHeader = true);

/// <summary>
/// Applies an AutoFilter by setting row <b>visibility</b> — it never deletes data (PRD §8.12).
/// A row is hidden when any column's criteria reject its value; Clear shows all rows again.
/// </summary>
public static class FilterEngine
{
    public static void Apply(Worksheet sheet, CellRange range, FilterSpec spec, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(spec);

        int firstDataRow = range.Top + (spec.HasHeader ? 1 : 0);
        for (int row = firstDataRow; row <= range.Bottom; row++)
        {
            bool visible = true;
            foreach (var (column, filter) in spec.Columns)
            {
                if (!filter.Matches(sheet.GetValue(row, column), culture))
                {
                    visible = false;
                    break;
                }
            }

            sheet.GetOrCreateRow(row).Hidden = !visible;
        }
    }

    public static void Clear(Worksheet sheet, CellRange range)
    {
        for (int row = range.Top; row <= range.Bottom; row++)
        {
            if (sheet.FindRow(row) is { } model)
            {
                model.Hidden = false;
            }
        }
    }

    /// <summary>
    /// How many data rows in <paramref name="range"/> would stay visible under <paramref name="spec"/> —
    /// the same per-column AND test as <see cref="Apply"/>, but counted instead of applied so a dialog
    /// can preview "showing X of N rows" without mutating any <c>Row.Hidden</c> flag.
    /// </summary>
    public static int CountVisible(Worksheet sheet, CellRange range, FilterSpec spec, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(spec);

        int firstDataRow = range.Top + (spec.HasHeader ? 1 : 0);
        int visible = 0;
        for (int row = firstDataRow; row <= range.Bottom; row++)
        {
            bool ok = true;
            foreach (var (column, filter) in spec.Columns)
            {
                if (!filter.Matches(sheet.GetValue(row, column), culture))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                visible++;
            }
        }

        return visible;
    }

    /// <summary>The distinct values present in a column over a range (for building a checklist).</summary>
    public static IReadOnlyList<CellValue> DistinctValues(Worksheet sheet, CellRange range, int column, bool hasHeader) =>
        DistinctValues(sheet, range, column, hasHeader, otherFilters: null, CultureInfo.InvariantCulture);

    /// <summary>
    /// The distinct values in a column, narrowed to rows that survive every <em>other</em> column's
    /// criteria in <paramref name="otherFilters"/> (the target column's own filter is ignored, so a
    /// user can still widen their own selection — mirrors Excel). With <paramref name="otherFilters"/>
    /// null this is the plain column-wide list. This is what makes a chained "filter A, then B" show,
    /// under B, only the values still reachable after A's filter.
    /// </summary>
    public static IReadOnlyList<CellValue> DistinctValues(
        Worksheet sheet, CellRange range, int column, bool hasHeader, FilterSpec? otherFilters, CultureInfo culture)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CellValue>();
        int firstDataRow = range.Top + (hasHeader ? 1 : 0);
        for (int row = firstDataRow; row <= range.Bottom; row++)
        {
            if (otherFilters is not null && !PassesOthers(sheet, row, column, otherFilters, culture))
            {
                continue;
            }

            var value = sheet.GetValue(row, column);
            if (seen.Add(value.ToInvariantString()))
            {
                result.Add(value);
            }
        }

        return result;
    }

    /// <summary>True when every column filter except <paramref name="exceptColumn"/> accepts the row.</summary>
    private static bool PassesOthers(Worksheet sheet, int row, int exceptColumn, FilterSpec spec, CultureInfo culture)
    {
        foreach (var (column, filter) in spec.Columns)
        {
            if (column == exceptColumn)
            {
                continue;
            }

            if (!filter.Matches(sheet.GetValue(row, column), culture))
            {
                return false;
            }
        }

        return true;
    }
}
