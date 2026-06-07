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

        return new Worksheet.State
        {
            Cells = cells,
            Rows = rows ? ShiftMap(before.Rows) : new Dictionary<int, Row>(before.Rows),
            Columns = rows ? new Dictionary<int, Column>(before.Columns) : ShiftMap(before.Columns),
            Merges = ShiftMerges(before.Merges, rows),
            FrozenRows = before.FrozenRows,
            FrozenColumns = before.FrozenColumns,
        };
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
            if (SurvivingExtremes(lo, hi) is not (int minSurv, int maxSurv))
            {
                continue; // entirely deleted
            }

            int newLo = MapCoord(minSurv)!.Value;
            int newHi = MapCoord(maxSurv)!.Value;
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
            int limit = _axis == StructuralAxis.Row ? SheetDimensions.MaxRows : SheetDimensions.MaxColumns;
            return coord >= _at ? Math.Min(coord + _count, limit - 1) : coord;
        }

        int deleteEnd = _at + _count;
        if (coord < _at)
        {
            return coord;
        }

        return coord < deleteEnd ? null : coord - _count;
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
