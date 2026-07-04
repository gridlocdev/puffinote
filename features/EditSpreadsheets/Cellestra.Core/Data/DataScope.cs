using Cellestra.Core.Model;

namespace Cellestra.Core.Data;

/// <summary>
/// Resolves the sort/filter state that belongs to a given data region, keeping a <b>table</b> and the
/// <b>sheet-level AutoFilter</b> as distinct, non-overlapping sources of truth. A table owns its filter
/// and sort on its own record (<see cref="Table.Filter"/>/<see cref="Table.Sort"/>); a bare range owns
/// them via <see cref="Worksheet.ActiveFilterSpec"/>/<see cref="Worksheet.ActiveSort"/>. This is what
/// lets a dropdown or the Sort &amp; Filter dialog show only the filters relevant to the clicked region
/// instead of leaking one region's state into another's.
/// </summary>
public static class DataScope
{
    /// <summary>The table whose range is exactly <paramref name="range"/>, or null when the range is a bare region.</summary>
    public static Table? TableAt(Worksheet sheet, CellRange range) =>
        sheet.Tables.FirstOrDefault(t => t.Range == range);

    /// <summary>The filter that applies to <paramref name="range"/> — the table's own, or the sheet AutoFilter's when it covers exactly this range.</summary>
    public static FilterSpec? EffectiveFilter(Worksheet sheet, CellRange range) =>
        TableAt(sheet, range) is { } table
            ? table.Filter
            : sheet.ActiveFilter == range ? sheet.ActiveFilterSpec : null;

    /// <summary>The sort that applies to <paramref name="range"/> — the table's own, or the sheet AutoFilter's when it covers exactly this range.</summary>
    public static SortSpec? EffectiveSort(Worksheet sheet, CellRange range) =>
        TableAt(sheet, range) is { } table
            ? table.Sort
            : sheet.ActiveFilter == range ? sheet.ActiveSort : null;
}
