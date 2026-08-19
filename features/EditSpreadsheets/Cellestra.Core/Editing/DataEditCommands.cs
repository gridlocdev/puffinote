using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;

namespace Cellestra.Core.Editing;

/// <summary>
/// Builders for undoable data operations (PRD §8.10–§8.15): sort, filter, remove-duplicates,
/// text-to-columns, tables, and validation. Cell-moving operations snapshot the affected range;
/// visibility/structure operations capture just what they touch.
/// </summary>
public static class DataEditCommands
{
    private static IReadOnlyList<(int, int)> Positions(CellRange range) => range.Positions().ToList();

    // ---- Sort -------------------------------------------------------------

    public static IEditCommand Sort(string sheet, CellRange range, SortSpec spec, CultureInfo? culture = null) =>
        new CellSnapshotCommand("Sort", sheet,
            ws => EditCommands.ClampedPositions(ws, range),
            ws => SortEngine.Sort(ws, EditCommands.ClampToUsed(ws, range) ?? range, spec, culture));

    /// <summary>Quick single-column sort by the active column, ascending or descending.</summary>
    public static IEditCommand SortByColumn(string sheet, CellRange range, int column, bool ascending, bool hasHeader = true, CultureInfo? culture = null) =>
        Sort(sheet, range, new SortSpec([new SortLevel(column, ascending)], hasHeader), culture);

    // ---- Remove duplicates ------------------------------------------------

    public static IEditCommand RemoveDuplicates(string sheet, CellRange range, IReadOnlyList<int> keyColumns, bool hasHeader, out Func<int> removedCount)
    {
        int removed = 0;
        removedCount = () => removed;
        return new CellSnapshotCommand("Remove Duplicates", sheet, Positions(range),
            ws => removed = DuplicateRemover.Remove(ws, range, keyColumns, hasHeader));
    }

    // ---- Text to columns --------------------------------------------------

    public static IEditCommand TextToColumns(Worksheet sheet, CellRange sourceColumn, char delimiter)
    {
        // Snapshot a generous block to the right so the split's output is fully reversible.
        int maxFields = EstimateColumns(sheet, sourceColumn, delimiter);
        var range = new CellRange(
            sourceColumn.TopLeft,
            new CellAddress(sourceColumn.Left + maxFields - 1, sourceColumn.Bottom));
        return new CellSnapshotCommand("Text to Columns", sheet.Name, Positions(range),
            ws => Data.TextToColumns.Split(ws, sourceColumn, delimiter));
    }

    private static int EstimateColumns(Worksheet sheet, CellRange sourceColumn, char delimiter)
    {
        int max = 1;
        for (int row = sourceColumn.Top; row <= sourceColumn.Bottom; row++)
        {
            var value = sheet.GetValue(row, sourceColumn.Left);
            if (value.IsText)
            {
                var rows = Serialization.Csv.CsvReader.Parse(value.AsText, new Serialization.Csv.CsvOptions { Delimiter = delimiter });
                if (rows.Count > 0)
                {
                    max = Math.Max(max, rows[0].Count);
                }
            }
        }

        return max;
    }

    // ---- Filter -----------------------------------------------------------

    public static IEditCommand ApplyFilter(string sheet, CellRange range, FilterSpec spec, CultureInfo culture) =>
        new RowVisibilityCommand("Filter", sheet, range, ws => FilterEngine.Apply(ws, range, spec, culture));

    public static IEditCommand ClearFilter(string sheet, CellRange range) =>
        new RowVisibilityCommand("Clear Filter", sheet, range, ws => FilterEngine.Clear(ws, range));

    /// <summary>
    /// Apply a whole sort-and-filter pipeline to a region as one undoable step (the Sort &amp; Filter
    /// dialog's commit, and the column dropdown). The sheet-level AutoFilter state
    /// (<see cref="Worksheet.ActiveSort"/>/<see cref="Worksheet.ActiveFilterSpec"/>/<see cref="Worksheet.ActiveFilter"/>)
    /// is assigned by the command's <b>first</b> step — before the visibility/sort steps so the sort's
    /// <c>ReapplyActiveFilter</c> re-hides against the new filter, not a stale one — and is captured for
    /// undo, so reverting drops the filter (and its chrome) together with the rows it hid. The visibility
    /// step runs <b>before</b> the sort so its <c>RowVisibilityCommand</c> snapshots the original
    /// (pre-shuffle) hidden flags. A null <paramref name="filter"/> clears filtering (unhides rows); a
    /// null/empty <paramref name="sort"/> leaves row order untouched.
    /// </summary>
    public static IEditCommand ApplySortAndFilter(string sheet, CellRange range, SortSpec? sort, FilterSpec? filter, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var steps = new List<IEditCommand>
        {
            SetSheetFilterState(sheet, (sort is not null || filter is not null) ? range : null, filter, sort),
            filter is not null ? ApplyFilter(sheet, range, filter, culture) : ClearFilter(sheet, range),
        };

        if (sort is { Levels.Count: > 0 })
        {
            steps.Add(Sort(sheet, range, sort, culture));
        }

        return EditCommands.Composite("Sort & Filter", steps);
    }

    /// <summary>
    /// Swap the sheet-level AutoFilter state to a new sort/filter/range, capturing the prior values for
    /// undo. Run as the first step of <see cref="ApplySortAndFilter"/> so the state is in place before
    /// the filter/sort steps read it, and so undo restores the previous filter (and its chrome) together
    /// with the row visibility those steps produced — rather than leaving a stale AutoFilter behind.
    /// </summary>
    private static IEditCommand SetSheetFilterState(string sheet, CellRange? filterRange, FilterSpec? filterSpec, SortSpec? sort)
    {
        CellRange? oldFilter = null;
        FilterSpec? oldFilterSpec = null;
        SortSpec? oldSort = null;
        return new ActionCommand("Filter",
            wb =>
            {
                var ws = Sheet(wb, sheet);
                oldFilter = ws.ActiveFilter;
                oldFilterSpec = ws.ActiveFilterSpec;
                oldSort = ws.ActiveSort;
                ws.ActiveFilter = filterRange;
                ws.ActiveFilterSpec = filterSpec;
                ws.ActiveSort = sort;
                return EditResult.None;
            },
            wb =>
            {
                var ws = Sheet(wb, sheet);
                ws.ActiveFilter = oldFilter;
                ws.ActiveFilterSpec = oldFilterSpec;
                ws.ActiveSort = oldSort;
                return EditResult.None;
            });
    }

    /// <summary>
    /// Apply a sort-and-filter pipeline to a <b>table</b>, storing the filter/sort on the table's own
    /// record (its database range) as the single source of truth — kept separate from the sheet-level
    /// AutoFilter so the two never double-write the same region. One undoable step: the table record is
    /// updated first (so the sort's <c>ReapplyActiveFilter</c>, which reads <see cref="Table.Filter"/>,
    /// re-hides against the new filter), then rows are hidden, then reordered. The visibility step
    /// snapshots the original hidden flags before the sort reshuffles them, so undo restores both.
    /// </summary>
    public static IEditCommand ApplyTableSortAndFilter(string sheet, Table table, SortSpec? sort, FilterSpec? filter, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        culture ??= CultureInfo.CurrentCulture;

        var range = table.Range;
        var steps = new List<IEditCommand>
        {
            ReplaceTable(sheet, table, table with { Filter = filter, Sort = sort }),
            filter is not null ? ApplyFilter(sheet, range, filter, culture) : ClearFilter(sheet, range),
        };

        if (sort is { Levels.Count: > 0 })
        {
            steps.Add(Sort(sheet, range, sort, culture));
        }

        return EditCommands.Composite("Sort & Filter", steps);
    }

    /// <summary>
    /// Clear the sheet-level structure that overlaps <paramref name="range"/> and which a cell-only
    /// <see cref="EditCommands.ClearAll"/> can't see: any table (database range — its filter dropdowns
    /// and header fill), merged regions, data validations, and the sheet AutoFilter when its region
    /// intersects the selection. A whole-column or whole-row selection also drops that line's fill and
    /// hidden flag (a background applied to an entire column/row lives there, not on the cells). Paired
    /// with <c>ClearAll</c> so "Clear All" over a selection wipes background colours and filters too.
    /// </summary>
    public static IEditCommand ClearRangeStructure(string sheet, CellRange range)
    {
        var removedTables = new List<Table>();
        var removedMerges = new List<MergedRegion>();
        var removedValidations = new List<DataValidation>();
        bool clearedFilter = false;
        CellRange? filter = null;
        FilterSpec? filterSpec = null;
        SortSpec? sort = null;
        var rowState = new List<(int Key, int StyleId, bool Hidden)>();
        var columnState = new List<(int Key, int StyleId, bool Hidden)>();

        return new ActionCommand("Clear All",
            wb =>
            {
                var ws = Sheet(wb, sheet);

                removedTables = [.. ws.Tables.Where(t => t.Range.Intersects(range))];
                foreach (var table in removedTables)
                {
                    ws.RemoveTable(table);
                }

                removedMerges = [.. ws.MergedRegions.Where(m => m.Range.Intersects(range))];
                if (removedMerges.Count > 0)
                {
                    ws.ReplaceMerges([.. ws.MergedRegions.Where(m => !m.Range.Intersects(range))]);
                }

                removedValidations = [.. ws.Validations.Where(v => v.Target.Intersects(range))];
                foreach (var validation in removedValidations)
                {
                    ws.RemoveValidation(validation);
                }

                clearedFilter = ws.ActiveFilter is { } active && active.Intersects(range);
                CellRange? filterRange = clearedFilter ? ws.ActiveFilter : null;
                if (clearedFilter)
                {
                    filter = ws.ActiveFilter;
                    filterSpec = ws.ActiveFilterSpec;
                    sort = ws.ActiveSort;
                }

                // A whole-column/row selection spans the entire axis; clearing such a selection also
                // resets that line's fill (background) and unhides it. Filtering hid rows via Row.Hidden,
                // so unhide the cleared filter's rows too.
                bool wholeColumns = range.Top == 0 && range.Bottom >= SheetDimensions.MaxRows - 1;
                bool wholeRows = range.Left == 0 && range.Right >= SheetDimensions.MaxColumns - 1;

                var touchedRows = new SortedSet<int>();
                if (wholeRows)
                {
                    foreach (int r in ws.Rows.Keys.Where(r => r >= range.Top && r <= range.Bottom))
                    {
                        touchedRows.Add(r);
                    }
                }

                if (filterRange is { } fr)
                {
                    foreach (int r in ws.Rows.Keys.Where(r => r >= fr.Top && r <= fr.Bottom && ws.IsRowHidden(r)))
                    {
                        touchedRows.Add(r);
                    }
                }

                var touchedColumns = wholeColumns
                    ? ws.Columns.Keys.Where(c => c >= range.Left && c <= range.Right).ToList()
                    : [];

                rowState = [.. touchedRows.Select(r =>
                {
                    var row = ws.GetOrCreateRow(r);
                    return (r, row.StyleId, row.Hidden);
                })];
                columnState = [.. touchedColumns.Select(c =>
                {
                    var col = ws.GetOrCreateColumn(c);
                    return (c, col.StyleId, col.Hidden);
                })];

                if (clearedFilter)
                {
                    ws.ActiveFilter = null;
                    ws.ActiveFilterSpec = null;
                    ws.ActiveSort = null;
                }

                foreach (int r in touchedRows)
                {
                    var row = ws.GetOrCreateRow(r);
                    if (wholeRows)
                    {
                        row.StyleId = StylePool.DefaultId;
                    }

                    row.Hidden = false; // unhide (whole-row clear, or a filtered-away row)
                }

                foreach (int c in touchedColumns)
                {
                    var column = ws.GetOrCreateColumn(c);
                    column.StyleId = StylePool.DefaultId;
                    column.Hidden = false;
                }

                return EditResult.None;
            },
            wb =>
            {
                var ws = Sheet(wb, sheet);

                foreach (var table in removedTables)
                {
                    ws.AddTable(table);
                }

                if (removedMerges.Count > 0)
                {
                    var merges = ws.MergedRegions.ToList();
                    merges.AddRange(removedMerges);
                    ws.ReplaceMerges(merges);
                }

                foreach (var validation in removedValidations)
                {
                    ws.AddValidation(validation);
                }

                if (clearedFilter)
                {
                    ws.ActiveFilter = filter;
                    ws.ActiveFilterSpec = filterSpec;
                    ws.ActiveSort = sort;
                }

                foreach (var (key, styleId, hidden) in rowState)
                {
                    var row = ws.GetOrCreateRow(key);
                    row.StyleId = styleId;
                    row.Hidden = hidden;
                }

                foreach (var (key, styleId, hidden) in columnState)
                {
                    var column = ws.GetOrCreateColumn(key);
                    column.StyleId = styleId;
                    column.Hidden = hidden;
                }

                return EditResult.None;
            });
    }

    /// <summary>
    /// Reset the sheet-level structure that <see cref="EditCommands.ClearAll"/> leaves behind:
    /// row/column fills and hidden flags; any active AutoFilter, filter conditions, and sort;
    /// every table (database range, which draws its own filter dropdowns and header fill); merged
    /// regions; data validations; and frozen panes. Row/column <em>dimensions</em> (widths/heights)
    /// are preserved — only fills and visibility are reset. Paired with <c>ClearAll</c> in the Reset
    /// Sheet Data step so a reset truly wipes background colours and filters, not just cell contents.
    /// </summary>
    public static IEditCommand ResetSheetLayout(string sheet)
    {
        // Captured on Apply so redo re-snapshots correctly (ActionCommand contract).
        var rowState = new List<(int Key, int StyleId, bool Hidden)>();
        var columnState = new List<(int Key, int StyleId, bool Hidden)>();
        CellRange? filter = null;
        FilterSpec? filterSpec = null;
        SortSpec? sort = null;
        var tables = new List<Table>();
        var merges = new List<MergedRegion>();
        var validations = new List<DataValidation>();
        int frozenRows = 0, frozenColumns = 0;

        return new ActionCommand("Reset Sheet Data",
            wb =>
            {
                var ws = Sheet(wb, sheet);
                rowState = [.. ws.Rows.Select(kv => (kv.Key, kv.Value.StyleId, kv.Value.Hidden))];
                columnState = [.. ws.Columns.Select(kv => (kv.Key, kv.Value.StyleId, kv.Value.Hidden))];
                filter = ws.ActiveFilter;
                filterSpec = ws.ActiveFilterSpec;
                sort = ws.ActiveSort;
                tables = [.. ws.Tables];
                merges = [.. ws.MergedRegions];
                validations = [.. ws.Validations];
                frozenRows = ws.FrozenRows;
                frozenColumns = ws.FrozenColumns;

                foreach (var row in ws.Rows.Values)
                {
                    row.StyleId = StylePool.DefaultId;
                    row.Hidden = false;
                }

                foreach (var column in ws.Columns.Values)
                {
                    column.StyleId = StylePool.DefaultId;
                    column.Hidden = false;
                }

                ws.ActiveFilter = null;
                ws.ActiveFilterSpec = null;
                ws.ActiveSort = null;
                foreach (var table in tables)
                {
                    ws.RemoveTable(table);
                }

                foreach (var validation in validations)
                {
                    ws.RemoveValidation(validation);
                }

                ws.ReplaceMerges([]);
                ws.FrozenRows = 0;
                ws.FrozenColumns = 0;
                return EditResult.None;
            },
            wb =>
            {
                var ws = Sheet(wb, sheet);
                foreach (var (key, styleId, hidden) in rowState)
                {
                    var row = ws.GetOrCreateRow(key);
                    row.StyleId = styleId;
                    row.Hidden = hidden;
                }

                foreach (var (key, styleId, hidden) in columnState)
                {
                    var column = ws.GetOrCreateColumn(key);
                    column.StyleId = styleId;
                    column.Hidden = hidden;
                }

                ws.ActiveFilter = filter;
                ws.ActiveFilterSpec = filterSpec;
                ws.ActiveSort = sort;
                foreach (var table in tables)
                {
                    ws.AddTable(table);
                }

                foreach (var validation in validations)
                {
                    ws.AddValidation(validation);
                }

                ws.ReplaceMerges(merges);
                ws.FrozenRows = frozenRows;
                ws.FrozenColumns = frozenColumns;
                return EditResult.None;
            });
    }

    // ---- Tables -----------------------------------------------------------

    public static IEditCommand CreateTable(string sheet, Table table) =>
        new ActionCommand("Create Table",
            wb =>
            {
                Sheet(wb, sheet).AddTable(table);
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).RemoveTable(table);
                return EditResult.None;
            });

    /// <summary>
    /// Create a table and, when it has a header row, bold that row so it stands out. One undoable
    /// step: the table is registered and the header cells are emboldened together. Bold is the only
    /// thing written into the cells — the header's grey band is theme chrome the grid paints from
    /// the table's flags (PRD §8.10), never a baked fill, so the table reads right in light and dark
    /// mode alike and no colour edit pollutes the document.
    /// </summary>
    public static IEditCommand CreateTable(Workbook workbook, string sheet, Table table)
    {
        var add = CreateTable(sheet, table);
        if (!table.HasHeader)
        {
            return add;
        }

        var headerRange = new CellRange(
            table.Range.TopLeft,
            new CellAddress(table.Range.Right, table.Range.Top));

        // Empty header cells get a placeholder name ("Column 1", "Column 2", …, left-to-right) so the
        // table always reads as labelled; cells that already carry a header are left as the user typed.
        int headerRow = table.Range.Top;
        int firstColumn = table.Range.Left;
        var fillHeaders = new CellSnapshotCommand("Create Table", sheet, Positions(headerRange),
            ws =>
            {
                for (int column = firstColumn; column <= table.Range.Right; column++)
                {
                    if (ws.GetValue(headerRow, column).IsEmpty)
                    {
                        ws.SetValue(headerRow, column, CellValue.Text($"Column {column - firstColumn + 1}"));
                    }
                }
            });

        var styleHeader = EditCommands.ApplyStyle(workbook, sheet, headerRange, StyleTableHeader, "Create Table");
        return EditCommands.Composite("Create Table", [add, fillHeaders, styleHeader]);
    }

    /// <summary>
    /// The header emphasis written into a table's header cells: <b>bold, and nothing else</b>. The
    /// grey band, its light/dark variants, alignment — all of that is the grid's chrome (PRD §8.10);
    /// only the emphasis is document formatting, so it survives a round-trip without freezing one
    /// theme's colours into the file.
    /// </summary>
    public static CellStyle StyleTableHeader(CellStyle style) =>
        style.WithFont(style.Font with { Bold = true });

    /// <summary>
    /// The inverse of <see cref="StyleTableHeader"/>: un-bold, touching nothing else — a fill or
    /// colour the user chose themselves is not this command's to take back.
    /// </summary>
    public static CellStyle ClearTableHeader(CellStyle style) =>
        style.WithFont(style.Font with { Bold = false });

    /// <summary>
    /// Designate (or clear) a table's first column as a header column (task 5): the first column's
    /// cells are bolded like the header row's, and the table's <see cref="Table.HasHeaderColumn"/>
    /// flag flips — which is what tells the grid to paint the theme's header-column band under them.
    /// One undoable step. The header-row cell at the corner is left to the row's own styling, so
    /// toggling the column never disturbs it.
    /// </summary>
    public static IEditCommand SetTableHeaderColumn(Workbook workbook, string sheet, Table table, bool enabled)
    {
        var replaced = table with { HasHeaderColumn = enabled };
        var flip = new ActionCommand("Header Column",
            wb =>
            {
                Sheet(wb, sheet).ReplaceTable(table, replaced);
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).ReplaceTable(replaced, table);
                return EditResult.None;
            });

        // The header row already styles the corner cell, so the column's run starts just below it.
        int top = table.Range.Top + (table.HasHeader ? 1 : 0);
        if (top > table.Range.Bottom)
        {
            return flip; // header-only table — nothing to restyle, just record the flag
        }

        var columnRange = new CellRange(
            new CellAddress(table.Range.Left, top),
            new CellAddress(table.Range.Left, table.Range.Bottom));
        var style = EditCommands.ApplyStyle(workbook, sheet, columnRange,
            enabled ? StyleTableHeader : ClearTableHeader, "Header Column");
        return EditCommands.Composite("Header Column", [flip, style]);
    }

    /// <summary>
    /// Turn a table's banded rows on or off (PRD §8.10). Pure flag flip, one undoable step: banding
    /// is chrome the grid paints from <see cref="Table.HasBandedRows"/> — a muted tint of the header
    /// band on alternate data rows — so there are no cells to restyle and nothing enters the undo
    /// history but the flag.
    /// </summary>
    public static IEditCommand SetTableBandedRows(string sheet, Table table, bool enabled)
    {
        var replaced = table with { HasBandedRows = enabled };
        return new ActionCommand("Banded Rows",
            wb =>
            {
                Sheet(wb, sheet).ReplaceTable(table, replaced);
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).ReplaceTable(replaced, table);
                return EditResult.None;
            });
    }

    public static IEditCommand RemoveTable(string sheet, Table table) =>
        new ActionCommand("Remove Table",
            wb =>
            {
                Sheet(wb, sheet).RemoveTable(table);
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).AddTable(table);
                return EditResult.None;
            });

    public static IEditCommand ReplaceTable(string sheet, Table existing, Table replacement) =>
        new ActionCommand("Resize Table",
            wb =>
            {
                Sheet(wb, sheet).ReplaceTable(existing, replacement);
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).ReplaceTable(replacement, existing);
                return EditResult.None;
            });

    /// <summary>
    /// Auto-expand a table to absorb an edit made in the row directly below it or the column
    /// directly to its right (PRD §8.10). Returns a single undoable command that grows the
    /// table's range and re-styles any newly-absorbed header cells, or <c>null</c> when the edit
    /// is not adjacent to the table (no expansion applies). The orchestrator calls this from the
    /// grid's commit path; this method does not wire the grid trigger itself.
    /// </summary>
    /// <remarks>
    /// Signature: <c>ExpandTableForEdit(Workbook workbook, string sheet, Table table, int editedRow, int editedColumn)</c>.
    /// </remarks>
    public static IEditCommand? ExpandTableForEdit(Workbook workbook, string sheet, Table table, int editedRow, int editedColumn)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(table);

        if (TableAutoExpand.ExpandFor(table, editedRow, editedColumn) is not { } grownRange)
        {
            return null; // the edit is not adjacent to the table — nothing to expand
        }

        var grown = table.WithRange(grownRange);
        var steps = new List<IEditCommand> { ReplaceTable(sheet, table, grown) };

        // A table absorbing a new line has to absorb its header cells too, or the header stops one row
        // (or one column) short of the table it labels — which reads as the band having a hole in it.
        //
        // Growing right adds a header *row* cell above each new column…
        if (table.HasHeader && grownRange.Right > table.Range.Right)
        {
            steps.Add(HeaderStyle(workbook, sheet,
                new CellRange(
                    new CellAddress(table.Range.Right + 1, grownRange.Top),
                    new CellAddress(grownRange.Right, grownRange.Top))));
        }

        // …and growing down adds a header *column* cell beside each new row, when the table has one.
        // The two are the same rule read along the other axis; only the row half had been written.
        if (table.HasHeaderColumn && grownRange.Bottom > table.Range.Bottom)
        {
            steps.Add(HeaderStyle(workbook, sheet,
                new CellRange(
                    new CellAddress(grownRange.Left, table.Range.Bottom + 1),
                    new CellAddress(grownRange.Left, grownRange.Bottom))));
        }

        return EditCommands.Composite("Expand Table", steps);
    }

    private static IEditCommand HeaderStyle(Workbook workbook, string sheet, CellRange range) =>
        EditCommands.ApplyStyle(workbook, sheet, range, StyleTableHeader, "Expand Table");

    // ---- Validation -------------------------------------------------------

    public static IEditCommand SetValidation(string sheet, DataValidation validation) =>
        new ActionCommand("Data Validation",
            wb =>
            {
                Sheet(wb, sheet).AddValidation(validation);
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).RemoveValidation(validation);
                return EditResult.None;
            });

    private static Worksheet Sheet(Workbook workbook, string name) =>
        workbook.FindSheet(name) ?? throw new InvalidOperationException($"Sheet '{name}' not found.");

    /// <summary>An undoable change to row visibility over a range (filters never touch cell data).</summary>
    private sealed class RowVisibilityCommand : IEditCommand
    {
        private readonly string _sheetName;
        private readonly CellRange _range;
        private readonly Action<Worksheet> _apply;
        private Dictionary<int, bool>? _before;

        public RowVisibilityCommand(string label, string sheetName, CellRange range, Action<Worksheet> apply)
        {
            Label = label;
            _sheetName = sheetName;
            _range = range;
            _apply = apply;
        }

        public string Label { get; }

        public EditResult Apply(Workbook workbook)
        {
            var sheet = Sheet(workbook, _sheetName);
            _before = [];
            for (int row = _range.Top; row <= _range.Bottom; row++)
            {
                _before[row] = sheet.IsRowHidden(row);
            }

            _apply(sheet);
            return EditResult.None;
        }

        public EditResult Undo(Workbook workbook)
        {
            if (_before is null)
            {
                return EditResult.None;
            }

            var sheet = Sheet(workbook, _sheetName);
            foreach (var (row, hidden) in _before)
            {
                sheet.GetOrCreateRow(row).Hidden = hidden;
            }

            return EditResult.None;
        }
    }
}
