using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Desktop.Services;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// Commits a sort/filter pipeline to the right place: a <b>table</b> stores it on its own record (via
/// <see cref="DataEditCommands.ApplyTableSortAndFilter"/>), a bare range stores it on the sheet
/// AutoFilter. Both the column dropdown and the Sort &amp; Filter dialog route through here so the two
/// surfaces stay consistent and never leave a region with two conflicting filter definitions.
/// </summary>
internal static class SortFilterCommit
{
    public static void Commit(WorkbookSession session, Worksheet sheet, CellRange range, SortSpec? sort, FilterSpec? filter, CultureInfo culture)
    {
        if (DataScope.TableAt(sheet, range) is { } table)
        {
            // Clear any stale sheet-level AutoFilter left over this exact range (e.g. from an older
            // file edited before tables owned their filter) so we don't draw two chevron sets.
            if (sheet.ActiveFilter == range)
            {
                sheet.ActiveFilter = null;
                sheet.ActiveFilterSpec = null;
                sheet.ActiveSort = null;
            }

            session.Execute(DataEditCommands.ApplyTableSortAndFilter(sheet.Name, table, sort, filter, culture));
            return;
        }

        // Bare-range AutoFilter: the command's first step assigns the sheet state (before its sort's
        // ReapplyActiveFilter reads it) and captures the prior values, so a single undo drops the
        // filter and its chrome together with the rows it hid (see ApplySortAndFilter).
        session.Execute(DataEditCommands.ApplySortAndFilter(sheet.Name, range, sort, filter, culture));
    }

    /// <summary>A filter spec carrying just one column's criteria (the simple dropdown's single-column model).</summary>
    public static FilterSpec SingleColumn(int column, ColumnFilter filter, bool hasHeader) =>
        new(new System.Collections.Generic.Dictionary<int, ColumnFilter> { [column] = filter }, hasHeader);
}
