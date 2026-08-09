using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// Where a clipboard block lands relative to the destination selection (behavior #4). The rules
/// mirror Excel/Calc: a single source cell fills a multi-cell selection; a block dropped on a
/// single active cell anchors at the top-left and expands; a block dropped on a selection that is
/// an exact multiple of its size tiles to fill it; anything else is a shape mismatch (pasted once
/// at the top-left, flagged so the UI can warn).
/// </summary>
public readonly record struct PastePlan(IReadOnlyList<(int Row, int Column)> Anchors, bool Tiled, bool ShapeMismatch);

public static class PastePlanner
{
    public static PastePlan Plan(int blockRows, int blockColumns, CellRange selection, bool isCut)
    {
        int selectionRows = selection.RowCount;
        int selectionColumns = selection.ColumnCount;

        // A move, or a paste onto a single active cell: anchor once at the top-left and expand.
        if (isCut || (selectionRows == 1 && selectionColumns == 1))
        {
            return new PastePlan([(selection.Top, selection.Left)], Tiled: false, ShapeMismatch: false);
        }

        // A single source cell fills the whole selection — unless the selection is the whole sheet, which
        // an expanding ⌘A reaches in two presses. Excel does fill all 17 billion cells there; we will not,
        // because "fill" means one live Cell per position and the sheet is sparse by design. Past the cap
        // the paste anchors at the top-left instead, which is bounded and undoable.
        if (blockRows == 1 && blockColumns == 1)
        {
            return IsOversized(selection)
                ? new PastePlan([(selection.Top, selection.Left)], Tiled: false, ShapeMismatch: false)
                : new PastePlan(EveryCell(selection), Tiled: true, ShapeMismatch: false);
        }

        // The selection is an exact multiple of the block: tile it. Bounded for the same reason as the fill
        // above — the whole sheet is an exact multiple of almost anything.
        if (!IsOversized(selection) && selectionRows % blockRows == 0 && selectionColumns % blockColumns == 0)
        {
            var anchors = new List<(int, int)>();
            for (int r = 0; r < selectionRows; r += blockRows)
            {
                for (int c = 0; c < selectionColumns; c += blockColumns)
                {
                    anchors.Add((selection.Top + r, selection.Left + c));
                }
            }

            return new PastePlan(anchors, Tiled: anchors.Count > 1, ShapeMismatch: false);
        }

        // Sizes don't reconcile: paste once at the top-left and flag the mismatch.
        return new PastePlan([(selection.Top, selection.Left)], Tiled: false, ShapeMismatch: true);
    }

    /// <summary>
    /// Whether a destination selection is too large to write a cell per position — the same threshold the
    /// in-place edits use (<see cref="EditCommands.OversizedCellCap"/>). Computed in <c>long</c> because
    /// <see cref="CellRange.CellCount"/> is an <c>int</c> and the whole sheet overflows it to zero, which
    /// is how an unbounded fill got past a capacity hint and into a loop with no end anyone would wait for.
    /// </summary>
    private static bool IsOversized(CellRange selection) =>
        (long)selection.RowCount * selection.ColumnCount > EditCommands.OversizedCellCap;

    private static List<(int, int)> EveryCell(CellRange selection)
    {
        var anchors = new List<(int, int)>(selection.CellCount);
        for (int r = selection.Top; r <= selection.Bottom; r++)
        {
            for (int c = selection.Left; c <= selection.Right; c++)
            {
                anchors.Add((r, c));
            }
        }

        return anchors;
    }
}
