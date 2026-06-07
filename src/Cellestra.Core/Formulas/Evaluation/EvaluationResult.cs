using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Evaluation;

/// <summary>
/// The outcome of evaluating a formula: the resulting <see cref="Value"/> and whether the
/// formula is <see cref="Volatile"/> (contains TODAY/NOW and must recompute every pass).
/// </summary>
public readonly record struct EvaluationResult(CellValue Value, bool Volatile)
{
    public static EvaluationResult From(CellValue value) => new(value, false);
}
