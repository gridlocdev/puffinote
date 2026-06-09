using Cellestra.Core.Dependencies;
using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Editing;

/// <summary>
/// Fill-handle drag and Fill Down/Right (PRD §8.15): a <b>copy-fill</b> that repeats the source
/// block across the extension of the fill range — values and styles copied, relative references
/// shifted, absolute ($) refs left put. v1 does not extrapolate series (a non-goal, §9); the
/// source pattern simply tiles. The source cells themselves are untouched; undo restores the rest.
/// </summary>
public sealed class FillCommand : IEditCommand
{
    private readonly string _sheetName;
    private readonly CellRange _source;
    private readonly CellRange _fill;
    private Dictionary<(int Row, int Column), Cell?>? _before;

    public FillCommand(string sheetName, CellRange source, CellRange fill)
    {
        _sheetName = sheetName;
        _source = source;
        _fill = fill;
        Label = "Fill";
    }

    public string Label { get; }

    public EditResult Apply(Workbook workbook)
    {
        var sheet = workbook.FindSheet(_sheetName)
            ?? throw new InvalidOperationException($"Sheet '{_sheetName}' not found.");

        var block = ClipboardBlock.Capture(sheet, _source, isCut: false).Cells;
        int height = _source.RowCount;
        int width = _source.ColumnCount;

        _before = new Dictionary<(int, int), Cell?>();
        foreach (var (row, column) in _fill.Positions())
        {
            if (_source.Contains(row, column))
            {
                continue; // the source stays as-is
            }

            _before[(row, column)] = sheet.Find(row, column)?.Clone();

            int sourceRow = _source.Top + Mod(row - _source.Top, height);
            int sourceColumn = _source.Left + Mod(column - _source.Left, width);
            var source = block[sourceRow - _source.Top, sourceColumn - _source.Left];
            WriteCell(sheet, row, column, source, row - sourceRow, column - sourceColumn);
        }

        return EditResult.Cells(ChangedRefs());
    }

    public EditResult Undo(Workbook workbook)
    {
        if (_before is null)
        {
            return EditResult.None;
        }

        var sheet = workbook.FindSheet(_sheetName);
        foreach (var ((row, column), before) in _before)
        {
            sheet?.PutCell(row, column, before?.Clone());
        }

        return EditResult.Cells(ChangedRefs());
    }

    /// <summary>
    /// The cells this fill would write, as <c>(row, column, value, isFormula)</c>, computed without
    /// mutating the sheet — used to pre-flight data validation before the fill runs. The source cells
    /// stay put (skipped); a blank source clears its destination (skipped). Mirrors <see cref="Apply"/>'s
    /// tiling so the projection cannot drift from what is actually written.
    /// </summary>
    public IEnumerable<(int Row, int Column, CellValue Value, bool IsFormula)> ProjectWrites(Workbook workbook)
    {
        var sheet = workbook.FindSheet(_sheetName);
        if (sheet is null)
        {
            yield break;
        }

        var block = ClipboardBlock.Capture(sheet, _source, isCut: false).Cells;
        int height = _source.RowCount;
        int width = _source.ColumnCount;
        foreach (var (row, column) in _fill.Positions())
        {
            if (_source.Contains(row, column))
            {
                continue;
            }

            int sourceRow = _source.Top + Mod(row - _source.Top, height);
            int sourceColumn = _source.Left + Mod(column - _source.Left, width);
            var source = block[sourceRow - _source.Top, sourceColumn - _source.Left];
            if (source is null)
            {
                continue;
            }

            yield return (row, column, source.Value, source.Formula is not null);
        }
    }

    private List<CellRef> ChangedRefs() =>
        _before!.Keys.Select(k => new CellRef(_sheetName, k.Row, k.Column)).ToList();

    private static void WriteCell(Worksheet sheet, int row, int column, ClipboardCell? source, int rowDelta, int columnDelta)
    {
        if (source is null)
        {
            sheet.PutCell(row, column, null);
            return;
        }

        string? formula = source.Formula;
        if (formula is not null && Parser.TryParse(formula, out var expr, out _) && expr is not null)
        {
            formula = FormulaWriter.Write(ReferenceAdjuster.ForCopy(expr, columnDelta, rowDelta));
        }

        sheet.PutCell(row, column, new Cell
        {
            Value = source.Value,
            Formula = formula,
            StyleId = source.StyleId,
        });
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
