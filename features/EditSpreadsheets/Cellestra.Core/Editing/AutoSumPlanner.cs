using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// One AutoSum proposal: write <see cref="Formula"/> into the cell at (<see cref="TargetRow"/>,
/// <see cref="TargetColumn"/>). The formula is an A1 <c>=SUM(range)</c> that the canonical
/// <see cref="EditCommands.SetInput(string,int,int,string,System.Globalization.CultureInfo)"/>
/// path translates to OpenFormula on commit.
/// </summary>
public readonly record struct AutoSumProposal(int TargetRow, int TargetColumn, string Formula);

/// <summary>
/// The standard spreadsheet AutoSum heuristic, kept pure and unit-tested (AGENTS.md: put real logic
/// in Core helpers, not the UI). Given the active cell, it proposes <c>=SUM(range)</c> over the
/// contiguous run of numbers directly <b>above</b> it; failing that, the run directly to its
/// <b>left</b>. Given a multi-cell selection of numbers, it proposes one <c>=SUM(...)</c> per column
/// in the row just below the selection (Excel's column-total behaviour).
/// </summary>
public static class AutoSumPlanner
{
    /// <summary>
    /// Plan the AutoSum edits for a selection on <paramref name="sheet"/>. The active cell only
    /// matters for the single-cell case. Returns an empty list when there is nothing sensible to sum
    /// (e.g. an isolated empty cell with no numbers above or to the left).
    /// </summary>
    public static IReadOnlyList<AutoSumProposal> Plan(Worksheet sheet, CellRange selection, int activeRow, int activeColumn)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        // A multi-cell selection of values: total each column into the row below the selection.
        if (!selection.IsSingleCell && SelectionHasNumbers(sheet, selection))
        {
            return PlanColumnTotals(sheet, selection);
        }

        return PlanSingleCell(sheet, activeRow, activeColumn);
    }

    private static IReadOnlyList<AutoSumProposal> PlanSingleCell(Worksheet sheet, int row, int column)
    {
        // Prefer the contiguous run of numbers directly above; otherwise the run directly to the left.
        if (RunAbove(sheet, row, column) is { } above)
        {
            return [new AutoSumProposal(row, column, SumFormula(above))];
        }

        if (RunLeft(sheet, row, column) is { } left)
        {
            return [new AutoSumProposal(row, column, SumFormula(left))];
        }

        return [];
    }

    private static IReadOnlyList<AutoSumProposal> PlanColumnTotals(Worksheet sheet, CellRange selection)
    {
        int targetRow = selection.Bottom + 1;
        if (targetRow >= SheetDimensions.MaxRows)
        {
            return [];
        }

        var proposals = new List<AutoSumProposal>();
        for (int column = selection.Left; column <= selection.Right; column++)
        {
            if (!ColumnHasNumber(sheet, selection, column))
            {
                continue; // skip non-numeric columns (e.g. a label column in the selection)
            }

            var range = new CellRange(
                new CellAddress(column, selection.Top),
                new CellAddress(column, selection.Bottom));
            proposals.Add(new AutoSumProposal(targetRow, column, SumFormula(range)));
        }

        return proposals;
    }

    /// <summary>The contiguous run of numbers immediately above (row-1, row-2, …); null if none.</summary>
    private static CellRange? RunAbove(Worksheet sheet, int row, int column)
    {
        int top = row;
        while (top - 1 >= 0 && sheet.GetValue(top - 1, column).IsNumber)
        {
            top--;
        }

        if (top == row)
        {
            return null; // the cell directly above isn't a number
        }

        return new CellRange(new CellAddress(column, top), new CellAddress(column, row - 1));
    }

    /// <summary>The contiguous run of numbers immediately to the left; null if none.</summary>
    private static CellRange? RunLeft(Worksheet sheet, int row, int column)
    {
        int left = column;
        while (left - 1 >= 0 && sheet.GetValue(row, left - 1).IsNumber)
        {
            left--;
        }

        if (left == column)
        {
            return null; // the cell directly to the left isn't a number
        }

        return new CellRange(new CellAddress(left, row), new CellAddress(column - 1, row));
    }

    private static bool SelectionHasNumbers(Worksheet sheet, CellRange selection)
    {
        foreach (var (row, column) in selection.Positions())
        {
            if (sheet.GetValue(row, column).IsNumber)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ColumnHasNumber(Worksheet sheet, CellRange selection, int column)
    {
        for (int row = selection.Top; row <= selection.Bottom; row++)
        {
            if (sheet.GetValue(row, column).IsNumber)
            {
                return true;
            }
        }

        return false;
    }

    private static string SumFormula(CellRange range) => $"=SUM({range})";
}
