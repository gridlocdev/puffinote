using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Model;

namespace Cellestra.Core.Dependencies;

/// <summary>
/// Walks a formula AST to find the cells it reads (its precedents) and whether it is
/// volatile. Ranges and named ranges are expanded to their member cells so the dependency
/// graph can answer "what changed affects whom" at cell granularity.
/// </summary>
public static class ReferenceCollector
{
    /// <summary>Largest range that is expanded cell-by-cell; bigger precedent ranges are summarized by their corners only.</summary>
    public const int MaxExpandedCells = 200_000;

    /// <summary>
    /// Collect the cells a formula reads. Ranges up to <see cref="MaxExpandedCells"/> are expanded
    /// cell-by-cell into <paramref name="destination"/>; a larger range is summarized by its corners
    /// (to bound graph size) and, when <paramref name="largeRanges"/> is supplied, also recorded
    /// there in full so the engine can still detect edits to its interior.
    /// </summary>
    public static void Collect(
        Expr expr,
        string homeSheet,
        Func<string, CellRange?> resolveName,
        ISet<CellRef> destination,
        ICollection<RangeRef>? largeRanges = null)
    {
        switch (expr)
        {
            case ReferenceExpr reference:
                destination.Add(ToRef(reference.Address, homeSheet));
                break;

            case RangeExpr range:
                Expand(range.Range, homeSheet, destination, largeRanges);
                break;

            case NameExpr name when resolveName(name.Name) is { } namedRange:
                Expand(namedRange, homeSheet, destination, largeRanges);
                break;

            case UnaryExpr unary:
                Collect(unary.Operand, homeSheet, resolveName, destination, largeRanges);
                break;

            case BinaryExpr binary:
                Collect(binary.Left, homeSheet, resolveName, destination, largeRanges);
                Collect(binary.Right, homeSheet, resolveName, destination, largeRanges);
                break;

            case FunctionCallExpr call:
                foreach (var argument in call.Arguments)
                {
                    Collect(argument, homeSheet, resolveName, destination, largeRanges);
                }

                break;
        }
    }

    public static bool IsVolatile(Expr expr) => expr switch
    {
        FunctionCallExpr call =>
            FunctionRegistry.VolatileFunctions.Contains(call.Name) ||
            call.Arguments.Any(IsVolatile),
        UnaryExpr unary => IsVolatile(unary.Operand),
        BinaryExpr binary => IsVolatile(binary.Left) || IsVolatile(binary.Right),
        _ => false,
    };

    private static void Expand(CellRange range, string homeSheet, ISet<CellRef> destination, ICollection<RangeRef>? largeRanges)
    {
        string sheet = range.Sheet ?? homeSheet;
        if (range.CellCount > MaxExpandedCells)
        {
            // Summarize a huge range by its corners to bound graph size, and record the full extent
            // so an edit to any interior cell still marks this formula for recalculation.
            destination.Add(new CellRef(sheet, range.Top, range.Left));
            destination.Add(new CellRef(sheet, range.Bottom, range.Right));
            largeRanges?.Add(new RangeRef(sheet, range.Top, range.Left, range.Bottom, range.Right));
            return;
        }

        foreach (var (row, column) in range.Positions())
        {
            destination.Add(new CellRef(sheet, row, column));
        }
    }

    private static CellRef ToRef(CellAddress address, string homeSheet) =>
        new(address.Sheet ?? homeSheet, address.Row, address.Column);
}

/// <summary>
/// A resolved rectangular precedent range too large to expand into the dependency graph cell by
/// cell. The engine keeps these so an edit inside the rectangle still marks its dependent formula.
/// </summary>
public readonly record struct RangeRef(string Sheet, int Top, int Left, int Bottom, int Right)
{
    public bool Contains(CellRef cell) =>
        string.Equals(cell.Sheet, Sheet, StringComparison.OrdinalIgnoreCase) &&
        cell.Row >= Top && cell.Row <= Bottom &&
        cell.Column >= Left && cell.Column <= Right;
}
