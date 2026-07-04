using Cellestra.Core.Dependencies;
using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// Swaps two equal-shaped, non-overlapping blocks of cells — the ⌥⇧-arrow nudge onto an occupied
/// neighbour. Exchanging is <b>non-destructive</b> (nothing is overwritten), and references follow
/// symmetrically through <see cref="ReferenceAdjuster.ForSwap"/> (refs into block A move to B and
/// vice-versa, Golden #8), so the cell that moves in can never hijack a formula that pointed at the
/// cell that moved out. The swap is its own inverse; undo restores the captured snapshot.
/// </summary>
public sealed class SwapCellsCommand : IEditCommand
{
    private readonly string _sheetName;
    private readonly CellRange _blockA;
    private readonly int _rowDelta;
    private readonly int _columnDelta;
    private readonly IReadOnlyCollection<CellRef>? _affectedFormulas;
    private Dictionary<(string Sheet, int Row, int Column), Cell?>? _before;

    /// <param name="affectedFormulas">
    /// When supplied (from the recalc engine's reverse index), only these formula cells are checked for
    /// references to rewrite — the fast path that avoids scanning the workbook. They are rewritten in
    /// place <em>before</em> the physical swap so a referencing cell inside a block carries its updated
    /// formula to its new home. Null falls back to a full scan (for callers without a dependency graph,
    /// e.g. unit tests).
    /// </param>
    public SwapCellsCommand(string sheetName, CellRange blockA, int rowDelta, int columnDelta,
        IReadOnlyCollection<CellRef>? affectedFormulas = null)
    {
        _sheetName = sheetName;
        _blockA = blockA;
        _rowDelta = rowDelta;
        _columnDelta = columnDelta;
        _affectedFormulas = affectedFormulas;
        Label = "Move";
    }

    public string Label { get; }

    public EditResult Apply(Workbook workbook)
    {
        var sheet = workbook.FindSheet(_sheetName)
            ?? throw new InvalidOperationException($"Sheet '{_sheetName}' not found.");
        _before = [];

        int rows = _blockA.RowCount;
        int cols = _blockA.ColumnCount;

        // Snapshot both footprints for undo.
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Snapshot(sheet, _blockA.Top + r, _blockA.Left + c);
                Snapshot(sheet, _blockA.Top + r + _rowDelta, _blockA.Left + c + _columnDelta);
            }
        }

        // Fast path: rewrite just the formulas the engine says reference either block, in place and
        // before the swap, so a referencing cell that sits inside a block carries its updated formula
        // along when it moves.
        if (_affectedFormulas is not null)
        {
            RewriteReferences(workbook, _affectedFormulas);
        }

        // Read both blocks, then exchange. The blocks are disjoint (the caller only swaps when the
        // destination doesn't overlap the source), so reads-then-writes can't clobber each other.
        var aCells = new Cell?[rows, cols];
        var bCells = new Cell?[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                aCells[r, c] = sheet.Find(_blockA.Top + r, _blockA.Left + c)?.Clone();
                bCells[r, c] = sheet.Find(_blockA.Top + r + _rowDelta, _blockA.Left + c + _columnDelta)?.Clone();
            }
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                sheet.PutCell(_blockA.Top + r, _blockA.Left + c, bCells[r, c]);
                sheet.PutCell(_blockA.Top + r + _rowDelta, _blockA.Left + c + _columnDelta, aCells[r, c]);
            }
        }

        // Fallback: no dependency graph supplied, so rewrite by scanning every formula post-swap.
        if (_affectedFormulas is null)
        {
            RewriteReferences(workbook, AllFormulaCells(workbook));
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

    /// <summary>
    /// Rewrite the given formula cells so references that targeted either swapped block now point at
    /// where that block's cells went (<see cref="ReferenceAdjuster.ForSwap"/>). Applying it to the
    /// swapped cells themselves fixes references between cells that moved together. Each changed cell is
    /// snapshotted (idempotently) for undo.
    /// </summary>
    private void RewriteReferences(Workbook workbook, IEnumerable<CellRef> cells)
    {
        foreach (var cellRef in cells)
        {
            var sheet = workbook.FindSheet(cellRef.Sheet);
            var cell = sheet?.Find(cellRef.Row, cellRef.Column);
            if (cell is null || !cell.IsFormula || !Parser.TryParse(cell.Formula!, out var expr, out _) || expr is null)
            {
                continue;
            }

            var adjusted = ReferenceAdjuster.ForSwap(expr, cellRef.Sheet, _sheetName, _blockA, _rowDelta, _columnDelta);
            string rewritten = FormulaWriter.Write(adjusted);
            if (!string.Equals(rewritten, cell.Formula, StringComparison.Ordinal))
            {
                Snapshot(sheet!, cellRef.Row, cellRef.Column);
                cell.Formula = rewritten;
            }
        }
    }

    private static IEnumerable<CellRef> AllFormulaCells(Workbook workbook)
    {
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var (row, column, cell) in sheet.EnumerateCells().ToList())
            {
                if (cell.IsFormula)
                {
                    yield return new CellRef(sheet.Name, row, column);
                }
            }
        }
    }

    private List<CellRef> ChangedRefs() =>
        [.. _before!.Keys.Select(k => new CellRef(k.Sheet, k.Row, k.Column))];

    private void Snapshot(Worksheet sheet, int row, int column)
    {
        var key = (sheet.Name, row, column);
        if (!_before!.ContainsKey(key))
        {
            _before[key] = sheet.Find(row, column)?.Clone();
        }
    }
}
