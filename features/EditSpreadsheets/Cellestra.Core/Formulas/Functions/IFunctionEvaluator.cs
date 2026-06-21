using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions;

/// <summary>
/// The slice of the evaluator a function implementation is allowed to use. Functions
/// receive their argument <see cref="Expr"/>s un-evaluated so they can short-circuit
/// (IF), trap errors (IFERROR), or read whole ranges (SUM, VLOOKUP) as needed.
/// </summary>
public interface IFunctionEvaluator
{
    IEvaluationContext Context { get; }

    /// <summary>Evaluate an argument to a single scalar value.</summary>
    CellValue Evaluate(Expr expression);

    /// <summary>
    /// Flatten an argument into scalar values: a range/named-range yields each of its
    /// cells (row-major); any other expression yields its single evaluated value.
    /// Error values are yielded as-is so aggregates can propagate them.
    /// </summary>
    IEnumerable<CellValue> EnumerateValues(Expr expression);

    /// <summary>Resolve an argument to a 2D range view. False when it is not a range/named range.</summary>
    bool TryEvaluateRange(Expr expression, out RangeValues range);
}
