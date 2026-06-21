using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions;

/// <summary>
/// An <see cref="IFunction"/> built from metadata plus a lambda body. Lets the v1 library be
/// declared compactly, one entry per function, while still flowing everything through the
/// single <see cref="FunctionRegistry"/> (Golden Rule #5).
/// </summary>
public sealed class DelegateFunction : IFunction
{
    private readonly Func<IReadOnlyList<Expr>, IFunctionEvaluator, CellValue> _body;

    public DelegateFunction(FunctionMetadata metadata, Func<IReadOnlyList<Expr>, IFunctionEvaluator, CellValue> body)
    {
        Metadata = metadata;
        _body = body;
    }

    public FunctionMetadata Metadata { get; }

    public CellValue Invoke(IReadOnlyList<Expr> arguments, IFunctionEvaluator evaluator) =>
        _body(arguments, evaluator);
}
