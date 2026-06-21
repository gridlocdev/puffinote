using Cellestra.Core.Dependencies;
using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// What a paste carries (Paste Special, PRD §4 / §6.2). <see cref="All"/> is the ordinary paste —
/// values, formulas, and styles together. The narrowed modes paste only one facet, leaving the rest
/// of each destination cell untouched: <see cref="Values"/> writes the source's computed value with
/// no formula and keeps the destination's style; <see cref="Formats"/> applies only the source's
/// style, leaving the destination's value/formula; <see cref="Formulas"/> writes the source's
/// formula text (and cached value) but keeps the destination's style.
/// </summary>
public enum PasteMode
{
    All,
    Values,
    Formats,
    Formulas,
}

/// <summary>
/// Pastes a <see cref="ClipboardBlock"/> (values, formulas, and styles) into a destination
/// selection, placing it per <see cref="PastePlanner"/> (fill / anchor / tile / mismatch). A copy
/// shifts each pasted formula's relative references by its offset from the source (Golden #8); a
/// cut moves the cells with their formulas unchanged and clears the source as part of the same
/// undoable step. A <see cref="PasteMode"/> narrows what is written (Paste Special). Undo restores
/// both the destination and (for a cut) the source.
/// </summary>
public sealed class PasteBlockCommand : IEditCommand
{
    private readonly string _destSheet;
    private readonly ClipboardBlock _block;
    private readonly PastePlan _plan;
    private readonly PasteMode _mode;
    private Dictionary<(string Sheet, int Row, int Column), Cell?>? _before;

    public PasteBlockCommand(string destSheet, CellRange destination, ClipboardBlock block, PasteMode mode = PasteMode.All)
    {
        _destSheet = destSheet;
        _block = block;
        _mode = mode;
        _plan = PastePlanner.Plan(block.Rows, block.Columns, destination, block.IsCut);
        Label = block.IsCut ? "Move" : mode == PasteMode.All ? "Paste" : "Paste Special";
    }

    /// <summary>Anchor a paste at a single cell (e.g. Return-to-paste); equivalent to a 1×1 selection.</summary>
    public PasteBlockCommand(string destSheet, int top, int left, ClipboardBlock block, PasteMode mode = PasteMode.All)
        : this(destSheet, new CellRange(new CellAddress(left, top), new CellAddress(left, top)), block, mode)
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

    /// <summary>
    /// The cells this paste would write, as <c>(row, column, value, isFormula)</c>, computed without
    /// mutating the sheet — used to pre-flight data validation before the paste runs. Paste Formats
    /// writes no value (yields nothing); a blank source clears its destination (an always-valid empty
    /// value) and is skipped. Formula targets are reported as such so the caller can skip them
    /// (formulas validate on recalc, not on entry).
    /// </summary>
    public IEnumerable<(int Row, int Column, Cellestra.Core.Values.CellValue Value, bool IsFormula)> ProjectWrites()
    {
        if (_mode == PasteMode.Formats)
        {
            yield break;
        }

        foreach (var (anchorRow, anchorColumn) in _plan.Anchors)
        {
            for (int r = 0; r < _block.Rows; r++)
            {
                for (int c = 0; c < _block.Columns; c++)
                {
                    var source = _block.Cells[r, c];
                    if (source is null)
                    {
                        continue;
                    }

                    bool isFormula = _mode != PasteMode.Values && source.Formula is not null;
                    yield return (anchorRow + r, anchorColumn + c, source.Value, isFormula);
                }
            }
        }
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
        // Paste Formats keeps the destination's value/formula and only re-skins it; a blank source
        // cell carries the default style, so its destination is reset to default too (Excel parity).
        if (_mode == PasteMode.Formats)
        {
            var existing = dest.Find(row, column);
            var cell = existing?.Clone() ?? new Cell();
            cell.StyleId = source?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId;
            dest.PutCell(row, column, cell.IsBlank ? null : cell);
            return;
        }

        if (source is null)
        {
            // Values/Formulas keep the destination's style on a blank source (only the content is
            // cleared); All wipes the cell entirely so a blank source clears the destination.
            if (_mode == PasteMode.All)
            {
                dest.PutCell(row, column, null);
            }
            else
            {
                var existing = dest.Find(row, column);
                if (existing is not null)
                {
                    existing.Formula = null;
                    existing.Value = Cellestra.Core.Values.CellValue.Empty;
                    dest.PutCell(row, column, existing.IsBlank ? null : existing);
                }
            }

            return;
        }

        // Resolve the formula to write (shifting relative refs on a copy; a cut moves verbatim).
        string? formula = source.Formula;
        if (formula is not null && !_block.IsCut &&
            Parser.TryParse(formula, out var expr, out _) && expr is not null)
        {
            formula = FormulaWriter.Write(ReferenceAdjuster.ForCopy(expr, columnDelta, rowDelta));
        }

        switch (_mode)
        {
            case PasteMode.Values:
                // Computed value only, no formula; keep the destination's style.
                WriteContent(dest, row, column, source.Value, formula: null, keepDestStyle: true, sourceStyle: source.StyleId);
                break;

            case PasteMode.Formulas:
                // Formula text + cached value, keep the destination's style.
                WriteContent(dest, row, column, source.Value, formula, keepDestStyle: true, sourceStyle: source.StyleId);
                break;

            default: // All
                WriteContent(dest, row, column, source.Value, formula, keepDestStyle: false, sourceStyle: source.StyleId);
                break;
        }
    }

    private static void WriteContent(Worksheet dest, int row, int column, Cellestra.Core.Values.CellValue value, string? formula, bool keepDestStyle, int sourceStyle)
    {
        int styleId = keepDestStyle ? (dest.Find(row, column)?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId) : sourceStyle;
        dest.PutCell(row, column, new Cell
        {
            Value = value,
            Formula = formula,
            StyleId = styleId,
        });
    }
}
