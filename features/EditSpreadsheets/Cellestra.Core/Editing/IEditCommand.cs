using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>What an edit changed, so the recalculation engine can respond appropriately.</summary>
public readonly record struct EditResult(IReadOnlyCollection<CellRef> ChangedCells, bool Structural)
{
    public static EditResult None { get; } = new([], false);

    public static EditResult Cells(IReadOnlyCollection<CellRef> changed) => new(changed, false);

    /// <summary>A structural change (insert/delete row/column) — the dependency graph should be rebuilt.</summary>
    public static EditResult StructuralChange { get; } = new([], true);
}

/// <summary>
/// A single undoable change to the workbook (ARCHITECTURE §5). The model mutation lives here;
/// the <see cref="EditHistory"/> owns the stacks. <see cref="Apply"/> performs the change and
/// captures whatever <see cref="Undo"/> needs to reverse it.
/// </summary>
public interface IEditCommand
{
    /// <summary>Human-readable label for the menu, e.g. "Set Value" → "Undo Set Value".</summary>
    string Label { get; }

    EditResult Apply(Workbook workbook);

    EditResult Undo(Workbook workbook);
}
