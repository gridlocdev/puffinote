using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions;

/// <summary>
/// A spreadsheet function. The single source of truth for everything about it is its
/// <see cref="Metadata"/> — the evaluator checks arity against it, autocomplete reads its
/// argument hints, and the Function Reference renders it as docs (ARCHITECTURE §5; one
/// registry, no second list — Golden Rule #5).
/// </summary>
public interface IFunction
{
    FunctionMetadata Metadata { get; }

    /// <summary>
    /// Compute the result. <paramref name="arguments"/> are the raw argument expressions;
    /// use <paramref name="evaluator"/> to evaluate them (scalar, flattened, or as a range).
    /// Return a <see cref="CellValue"/> error rather than throwing for user-facing problems.
    /// </summary>
    CellValue Invoke(IReadOnlyList<Expr> arguments, IFunctionEvaluator evaluator);
}
