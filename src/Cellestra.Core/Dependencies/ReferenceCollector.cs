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

    public static void Collect(Expr expr, string homeSheet, Func<string, CellRange?> resolveName, ISet<CellRef> destination)
    {
        switch (expr)
        {
            case ReferenceExpr reference:
                destination.Add(ToRef(reference.Address, homeSheet));
                break;

            case RangeExpr range:
                Expand(range.Range, homeSheet, destination);
                break;

            case NameExpr name when resolveName(name.Name) is { } namedRange:
                Expand(namedRange, homeSheet, destination);
                break;

            case UnaryExpr unary:
                Collect(unary.Operand, homeSheet, resolveName, destination);
                break;

            case BinaryExpr binary:
                Collect(binary.Left, homeSheet, resolveName, destination);
                Collect(binary.Right, homeSheet, resolveName, destination);
                break;

            case FunctionCallExpr call:
                foreach (var argument in call.Arguments)
                {
                    Collect(argument, homeSheet, resolveName, destination);
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

    private static void Expand(CellRange range, string homeSheet, ISet<CellRef> destination)
    {
        string sheet = range.Sheet ?? homeSheet;
        if (range.CellCount > MaxExpandedCells)
        {
            // Summarize a huge range by its four corners to bound graph size.
            destination.Add(new CellRef(sheet, range.Top, range.Left));
            destination.Add(new CellRef(sheet, range.Bottom, range.Right));
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
