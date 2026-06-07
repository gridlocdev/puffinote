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

    /// <summary>The distinct values present in a column over a range (for building a checklist).</summary>
    public static IReadOnlyList<CellValue> DistinctValues(Worksheet sheet, CellRange range, int column, bool hasHeader)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CellValue>();
        int firstDataRow = range.Top + (hasHeader ? 1 : 0);
        for (int row = firstDataRow; row <= range.Bottom; row++)
        {
            var value = sheet.GetValue(row, column);
            if (seen.Add(value.ToInvariantString()))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
