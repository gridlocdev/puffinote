using Cellestra.Core.Data;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

public enum StructuralAxis
{
    Row,
    Column,
}

public enum StructuralOperation
{
    Insert,
    Delete,
}

/// <summary>
/// Inserts or deletes rows/columns: shifts cells, sizing, and merges on the edited sheet, and
/// rewrites every formula in the workbook through <see cref="ReferenceAdjuster"/> (Golden #8).
/// Undo restores the edited sheet's snapshot and the other sheets' formula text.
/// </summary>
public sealed class StructuralEditCommand : IEditCommand
{
    private readonly string _sheetName;
    private readonly StructuralAxis _axis;
    private readonly StructuralOperation _operation;
    private readonly int _at;
    private readonly int _count;

    private Worksheet.State? _editedBefore;
    private Dictionary<CellRef, string>? _otherFormulasBefore;

    public StructuralEditCommand(string sheetName, StructuralAxis axis, StructuralOperation operation, int at, int count)
    {
        _sheetName = sheetName;
        _axis = axis;
        _operation = operation;
        _at = at;
        _count = count;
        Label = $"{(operation == StructuralOperation.Insert ? "Insert" : "Delete")} {(axis == StructuralAxis.Row ? "Rows" : "Columns")}";
    }

    public string Label { get; }

    public EditResult Apply(Workbook workbook)
    {
        var sheet = Sheet(workbook);
        _editedBefore = sheet.Capture();
        _otherFormulasBefore = CaptureOtherFormulas(workbook);

        sheet.Restore(BuildShiftedState(_editedBefore));
        AdjustFormulas(workbook);
        return EditResult.StructuralChange;
    }

    public EditResult Undo(Workbook workbook)
    {
        if (_editedBefore is null || _otherFormulasBefore is null)
        {
            return EditResult.None;
        }

        Sheet(workbook).Restore(_editedBefore);
        foreach (var (cellRef, formula) in _otherFormulasBefore)
        {
            var cell = workbook.FindSheet(cellRef.Sheet)?.Find(cellRef.Row, cellRef.Column);
            if (cell is not null)
            {
                cell.Formula = formula;
            }
        }

        return EditResult.StructuralChange;
    }

    // ---- Build the post-shift sheet state --------------------------------

    private Worksheet.State BuildShiftedState(Worksheet.State before)
    {
        bool rows = _axis == StructuralAxis.Row;

        var cells = new Dictionary<(int, int), Cell>();
        foreach (var ((row, column), cell) in before.Cells)
        {
            int coord = rows ? row : column;
            int? mapped = MapCoord(coord);
            if (mapped is null)
            {
                continue;
            }

            var key = rows ? (mapped.Value, column) : (row, mapped.Value);
            cells[key] = cell;
        }

        var shiftedRows = rows ? ShiftMap(before.Rows) : new Dictionary<int, Row>(before.Rows);
        var shiftedColumns = rows ? new Dictionary<int, Column>(before.Columns) : ShiftMap(before.Columns);

        // Inserted rows/columns inherit the formatting of the line just before them (PRD §8.14):
        // the row-wide/column-wide style plus the per-cell styles along that source line, with
        // empty values. Inserting at the very top/left (_at == 0) has no source, so no inheritance.
        if (_operation == StructuralOperation.Insert && _at > 0)
        {
            InheritFormatting(before, cells, shiftedRows, shiftedColumns, rows);
        }

        return new Worksheet.State
        {
            Cells = cells,
            Rows = shiftedRows,
            Columns = shiftedColumns,
            Merges = ShiftMerges(before.Merges, rows),
            Tables = ShiftTables(before.Tables, rows),
            FrozenRows = before.FrozenRows,
            FrozenColumns = before.FrozenColumns,
        };
    }

    // A row/column edit that overlaps a table resizes it (delete shrinks, insert-within grows) along
    // the edited axis; a table entirely inside a deleted span is removed. The cross axis is untouched.
    // Mirrors ShiftMerges' coordinate math so tables and merges shift consistently. Undo restores the
    // original tables because they live in the captured State.
    private List<Table> ShiftTables(List<Table> source, bool rows)
    {
        var result = new List<Table>();
        foreach (var table in source)
        {
            int lo = rows ? table.Range.Top : table.Range.Left;
            int hi = rows ? table.Range.Bottom : table.Range.Right;
            if (SurvivingExtremes(lo, hi) is not (int minSurv, int maxSurv) ||
                MapSpan(minSurv, maxSurv) is not (int newLo, int newHi))
            {
                continue; // the table's edited-axis span is entirely deleted or pushed off the sheet
            }

            var range = rows
                ? new CellRange(new CellAddress(table.Range.Left, newLo), new CellAddress(table.Range.Right, newHi))
                : new CellRange(new CellAddress(newLo, table.Range.Top), new CellAddress(newHi, table.Range.Bottom));

            result.Add(table.WithRange(range));
        }

        return result;
    }

    private void InheritFormatting(
        Worksheet.State before,
        Dictionary<(int, int), Cell> cells,
        Dictionary<int, Row> shiftedRows,
        Dictionary<int, Column> shiftedColumns,
        bool rows)
    {
        int source = _at - 1;

        // The line-wide default style (row-wide or column-wide).
        if (rows)
        {
            if (before.Rows.TryGetValue(source, out var srcRow) && srcRow.StyleId != 0)
            {
                for (int r = _at; r < _at + _count; r++)
                {
                    shiftedRows[r] = new Row { StyleId = srcRow.StyleId };
                }
            }
        }
        else if (before.Columns.TryGetValue(source, out var srcColumn) && srcColumn.StyleId != 0)
        {
            for (int c = _at; c < _at + _count; c++)
            {
                shiftedColumns[c] = new Column { StyleId = srcColumn.StyleId };
            }
        }

        // The per-cell styles along the source line, as empty styled cells.
        foreach (var ((row, column), cell) in before.Cells)
        {
            int line = rows ? row : column;
            if (line != source || cell.StyleId == 0)
            {
                continue;
            }

            int cross = rows ? column : row;
            for (int i = _at; i < _at + _count; i++)
            {
                var key = rows ? (i, cross) : (cross, i);
                if (!cells.ContainsKey(key))
                {
                    cells[key] = new Cell { StyleId = cell.StyleId };
                }
            }
        }
    }

    private Dictionary<int, T> ShiftMap<T>(IReadOnlyDictionary<int, T> source)
    {
        var result = new Dictionary<int, T>();
        foreach (var (key, value) in source)
        {
            if (MapCoord(key) is { } mapped)
            {
                result[mapped] = value;
            }
        }

        return result;
    }

    private List<MergedRegion> ShiftMerges(List<MergedRegion> source, bool rows)
    {
        var result = new List<MergedRegion>();
        foreach (var region in source)
        {
            int lo = rows ? region.Range.Top : region.Range.Left;
            int hi = rows ? region.Range.Bottom : region.Range.Right;
            if (SurvivingExtremes(lo, hi) is not (int minSurv, int maxSurv) ||
                MapSpan(minSurv, maxSurv) is not (int newLo, int newHi))
            {
                continue; // entirely deleted or pushed off the sheet
            }

            var range = rows
                ? new CellRange(new CellAddress(region.Range.Left, newLo), new CellAddress(region.Range.Right, newHi))
                : new CellRange(new CellAddress(newLo, region.Range.Top), new CellAddress(newHi, region.Range.Bottom));

            if (range.CellCount > 1)
            {
                result.Add(new MergedRegion(range));
            }
        }

        return result;
    }

    // ---- Formula adjustment ----------------------------------------------

    private void AdjustFormulas(Workbook workbook)
    {
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var (_, _, cell) in sheet.EnumerateCells().ToList())
            {
                if (!cell.IsFormula || !Parser.TryParse(cell.Formula!, out var expr, out _) || expr is null)
                {
                    continue;
                }

                var adjusted = (_axis, _operation) switch
                {
                    (StructuralAxis.Row, StructuralOperation.Insert) => ReferenceAdjuster.ForInsertRows(expr, sheet.Name, _sheetName, _at, _count),
                    (StructuralAxis.Row, StructuralOperation.Delete) => ReferenceAdjuster.ForDeleteRows(expr, sheet.Name, _sheetName, _at, _count),
                    (StructuralAxis.Column, StructuralOperation.Insert) => ReferenceAdjuster.ForInsertColumns(expr, sheet.Name, _sheetName, _at, _count),
                    _ => ReferenceAdjuster.ForDeleteColumns(expr, sheet.Name, _sheetName, _at, _count),
                };

                cell.Formula = FormulaWriter.Write(adjusted);
            }
        }
    }

    private Dictionary<CellRef, string> CaptureOtherFormulas(Workbook workbook)
    {
        var map = new Dictionary<CellRef, string>();
        foreach (var sheet in workbook.Worksheets)
        {
            if (string.Equals(sheet.Name, _sheetName, StringComparison.OrdinalIgnoreCase))
            {
                continue; // the edited sheet is restored from its snapshot
            }

            foreach (var (row, column, cell) in sheet.EnumerateCells())
            {
                if (cell.IsFormula)
                {
                    map[new CellRef(sheet.Name, row, column)] = cell.Formula!;
                }
            }
        }

        return map;
    }

    // ---- Coordinate mapping (mirrors ReferenceAdjuster.AxisEdit) ----------

    private int? MapCoord(int coord)
    {
        if (_operation == StructuralOperation.Insert)
        {
            if (coord < _at)
            {
                return coord;
            }

            // A cell pushed past the sheet's last line is dropped (null), not clamped onto the edge:
            // clamping collapsed several rows/columns onto limit-1 and they overwrote each other,
            // silently destroying data. Mirrors the delete branch returning null for removed lines.
            int limit = _axis == StructuralAxis.Row ? SheetDimensions.MaxRows : SheetDimensions.MaxColumns;
            int shifted = coord + _count;
            return shifted >= limit ? null : shifted;
        }

        int deleteEnd = _at + _count;
        if (coord < _at)
        {
            return coord;
        }

        return coord < deleteEnd ? null : coord - _count;
    }

    /// <summary>
    /// Map a surviving [min,max] span to its post-edit corners. On an insert the far corner can be
    /// pushed past the sheet edge: it clamps to the last line (the span shrinks), while a span
    /// pushed off entirely returns null so the caller drops it.
    /// </summary>
    private (int Lo, int Hi)? MapSpan(int min, int max)
    {
        if (MapCoord(min) is not { } lo)
        {
            return null;
        }

        int limit = _axis == StructuralAxis.Row ? SheetDimensions.MaxRows : SheetDimensions.MaxColumns;
        int hi = MapCoord(max) ?? (limit - 1);
        return (lo, hi);
    }

    private (int Min, int Max)? SurvivingExtremes(int from, int to)
    {
        if (_operation == StructuralOperation.Insert)
        {
            return (from, to);
        }

        int deleteEnd = _at + _count;
        int? min = from < _at ? from : (Math.Max(from, deleteEnd) <= to ? Math.Max(from, deleteEnd) : null);
        int? max = to >= deleteEnd ? to : (to < _at ? to : (_at - 1 >= from ? _at - 1 : null));
        return min is { } lo && max is { } hi ? (lo, hi) : null;
    }

    private Worksheet Sheet(Workbook workbook) =>
        workbook.FindSheet(_sheetName) ?? throw new InvalidOperationException($"Sheet '{_sheetName}' not found.");
}
