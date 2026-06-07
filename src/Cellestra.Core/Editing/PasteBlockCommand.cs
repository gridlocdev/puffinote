using Cellestra.Core.Dependencies;
using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// Pastes a <see cref="ClipboardBlock"/> (values, formulas, and styles) into a destination
/// selection, placing it per <see cref="PastePlanner"/> (fill / anchor / tile / mismatch). A copy
/// shifts each pasted formula's relative references by its offset from the source (Golden #8); a
/// cut moves the cells with their formulas unchanged and clears the source as part of the same
/// undoable step. Undo restores both the destination and (for a cut) the source.
/// </summary>
public sealed class PasteBlockCommand : IEditCommand
{
    private readonly string _destSheet;
    private readonly ClipboardBlock _block;
    private readonly PastePlan _plan;
    private Dictionary<(string Sheet, int Row, int Column), Cell?>? _before;

    public PasteBlockCommand(string destSheet, CellRange destination, ClipboardBlock block)
    {
        _destSheet = destSheet;
        _block = block;
        _plan = PastePlanner.Plan(block.Rows, block.Columns, destination, block.IsCut);
        Label = block.IsCut ? "Move" : "Paste";
    }

    /// <summary>Anchor a paste at a single cell (e.g. Return-to-paste); equivalent to a 1×1 selection.</summary>
    public PasteBlockCommand(string destSheet, int top, int left, ClipboardBlock block)
        : this(destSheet, new CellRange(new CellAddress(left, top), new CellAddress(left, top)), block)
    {
    }

    public string Label { get; }

    /// <summary>True when the block and selection sizes did not reconcile (pasted once; UI should warn).</summary>
    public bool ShapeMismatch => _plan.ShapeMismatch;

    public EditResult Apply(Workbook workbook)
    {
        var dest = workbook.FindSheet(_destSheet)
            ?? throw new InvalidOperationException($"Sheet '{_destSheet}' not found.");
        var source = workbook.FindSheet(_block.SourceSheet);

        _before = new Dictionary<(string, int, int), Cell?>();

        // Snapshot the source cells we are about to clear (cut only).
        if (_block.IsCut && source is not null)
        {
            foreach (var (row, column) in _block.SourceRange.Positions())
            {
                Snapshot(source, row, column);
            }
        }

        // Snapshot every destination footprint (deduped).
        foreach (var (anchorRow, anchorColumn) in _plan.Anchors)
        {
            for (int r = 0; r < _block.Rows; r++)
            {
                for (int c = 0; c < _block.Columns; c++)
                {
                    Snapshot(dest, anchorRow + r, anchorColumn + c);
                }
            }
        }

        // Clear the source for a move.
        if (_block.IsCut && source is not null)
        {
            foreach (var (row, column) in _block.SourceRange.Positions())
            {
                source.PutCell(row, column, null);
            }
        }

        // Write the block at each anchor; relative formula references shift by that anchor's offset.
        foreach (var (anchorRow, anchorColumn) in _plan.Anchors)
        {
            int rowDelta = anchorRow - _block.SourceRange.Top;
            int columnDelta = anchorColumn - _block.SourceRange.Left;
            for (int r = 0; r < _block.Rows; r++)
            {
                for (int c = 0; c < _block.Columns; c++)
                {
                    WriteCell(dest, anchorRow + r, anchorColumn + c, _block.Cells[r, c], rowDelta, columnDelta);
                }
            }
        }

        return EditResult.Cells(ChangedRefs());
    }

    public EditResult Undo(Workbook workbook)
    {
        if (_before is null)
        {
            return EditResult.None;
        }

        foreach (var ((sheetName, row, column), before) in _before)
        {
            workbook.FindSheet(sheetName)?.PutCell(row, column, before?.Clone());
        }

        return EditResult.Cells(ChangedRefs());
    }

    private List<CellRef> ChangedRefs() =>
        _before!.Keys.Select(k => new CellRef(k.Sheet, k.Row, k.Column)).ToList();

    private void Snapshot(Worksheet sheet, int row, int column)
    {
        var key = (sheet.Name, row, column);
        if (!_before!.ContainsKey(key))
        {
            _before[key] = sheet.Find(row, column)?.Clone();
        }
    }

    private void WriteCell(Worksheet dest, int row, int column, ClipboardCell? source, int rowDelta, int columnDelta)
    {
        if (source is null)
        {
            dest.PutCell(row, column, null); // a blank source cell clears the destination
            return;
        }

        string? formula = source.Formula;
        if (formula is not null && !_block.IsCut &&
            Parser.TryParse(formula, out var expr, out _) && expr is not null)
        {
            // Copy-fill: relative references shift; absolute ($) refs stay put. A cut moves verbatim.
            formula = FormulaWriter.Write(ReferenceAdjuster.ForCopy(expr, columnDelta, rowDelta));
        }

        dest.PutCell(row, column, new Cell
        {
            Value = source.Value,
            Formula = formula,
            StyleId = source.StyleId,
        });
    }
}
