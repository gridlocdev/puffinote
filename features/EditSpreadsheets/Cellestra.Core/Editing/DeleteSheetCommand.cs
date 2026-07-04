using Cellestra.Core.Dependencies;
using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// Deletes a worksheet and rewrites every formula on the surviving sheets that referenced it to
/// <c>#REF!</c> (Golden #8). Undo re-inserts the same worksheet instance at its original position
/// and restores the other sheets' formula text. The workbook must keep at least one sheet.
/// </summary>
public sealed class DeleteSheetCommand : IEditCommand
{
    private readonly string _sheetName;
    private int _index;
    private Worksheet? _removed;
    private Dictionary<CellRef, string>? _otherFormulasBefore;

    public DeleteSheetCommand(string sheetName)
    {
        _sheetName = sheetName;
        Label = "Delete Sheet";
    }

    public string Label { get; }

    public EditResult Apply(Workbook workbook)
    {
        var sheet = workbook.FindSheet(_sheetName)
            ?? throw new InvalidOperationException($"Sheet '{_sheetName}' not found.");

        _index = workbook.IndexOf(sheet);
        _removed = sheet;
        _otherFormulasBefore = CaptureOtherFormulas(workbook);

        workbook.RemoveSheet(sheet); // throws if this is the last sheet
        InvalidateReferences(workbook);
        return EditResult.StructuralChange;
    }

    public EditResult Undo(Workbook workbook)
    {
        if (_removed is null || _otherFormulasBefore is null)
        {
            return EditResult.None;
        }

        workbook.InsertSheet(_index, _removed);
        foreach (var (cellRef, formula) in _otherFormulasBefore)
        {
            var cell = workbook.FindSheet(cellRef.Sheet)?.Find(cellRef.Row, cellRef.Column);
            cell?.Formula = formula;
        }

        return EditResult.StructuralChange;
    }

    private void InvalidateReferences(Workbook workbook)
    {
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var (_, _, cell) in sheet.EnumerateCells().ToList())
            {
                if (!cell.IsFormula || !Parser.TryParse(cell.Formula!, out var expr, out _) || expr is null)
                {
                    continue;
                }

                cell.Formula = FormulaWriter.Write(ReferenceAdjuster.ForDeleteSheet(expr, _sheetName, sheet.Name));
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
                continue; // the removed sheet is held intact for re-insertion on undo
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
}
