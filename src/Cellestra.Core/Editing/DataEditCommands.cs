using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;

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

    public static IEditCommand Sort(string sheet, CellRange range, SortSpec spec) =>
        new CellSnapshotCommand("Sort", sheet, Positions(range), ws => SortEngine.Sort(ws, range, spec));

    /// <summary>Quick single-column sort by the active column, ascending or descending.</summary>
    public static IEditCommand SortByColumn(string sheet, CellRange range, int column, bool ascending, bool hasHeader = true) =>
        Sort(sheet, range, new SortSpec(new[] { new SortLevel(column, ascending) }, hasHeader));

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
    /// Create a table and, when it has a header row, give that row a sensible contrasting default
    /// style (the green accent fill with bold white text) so it stands out. One undoable step: the
    /// table is registered and the header cells are restyled together. Header styling is plain cell
    /// fills/fonts on the range — no structured references — so it round-trips to ODS.
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
        var styleHeader = EditCommands.ApplyStyle(workbook, sheet, headerRange, StyleTableHeader, "Create Table");
        return EditCommands.Composite("Create Table", new[] { add, styleHeader });
    }

    /// <summary>The default header style: bold white text on the green accent fill (PRD §8.10).</summary>
    public static CellStyle StyleTableHeader(CellStyle style)
    {
        var fill = Fill.Solid(new ArgbColor(Defaults.TableHeaderFillArgb));
        var font = style.Font with { Bold = true, Color = new ArgbColor(Defaults.TableHeaderTextArgb) };
        return style.WithFill(fill).WithFont(font);
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
            _before = new Dictionary<int, bool>();
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
