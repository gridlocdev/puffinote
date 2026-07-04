using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Evaluation;

/// <summary>
/// Walks a parsed formula and produces a <see cref="CellValue"/>. Errors are values, not
/// exceptions, so they flow through arithmetic and out to the cell (PRD §7 heuristic #9).
/// The same instance also serves functions as their <see cref="IFunctionEvaluator"/>.
/// </summary>
public sealed class Evaluator : IExprVisitor<CellValue>, IFunctionEvaluator
{
    private readonly FunctionRegistry _functions;

    public Evaluator(IEvaluationContext context, FunctionRegistry functions)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        _functions = functions ?? throw new ArgumentNullException(nameof(functions));
    }

    public IEvaluationContext Context { get; }

    public CellValue Evaluate(Expr expression) => expression.Accept(this);

    // ---- IExprVisitor ----------------------------------------------------

    public CellValue VisitLiteral(LiteralExpr node) => node.Value;

    public CellValue VisitReference(ReferenceExpr node) =>
        Context.GetCell(node.Address.Sheet, node.Address.Row, node.Address.Column);

    public CellValue VisitRange(RangeExpr node) =>
        CellValue.FromError(CellError.Value("A range is not valid in this position."));

    public CellValue VisitName(NameExpr node)
    {
        if (Context.TryResolveName(node.Name, out var range))
        {
            if (range.IsSingleCell)
            {
                return Context.GetCell(range.Sheet, range.Top, range.Left);
            }

            return CellValue.FromError(CellError.Value($"The name '{node.Name}' is a range, not a single value."));
        }

        return CellValue.FromError(CellError.Name($"Unknown name '{node.Name}'."));
    }

    public CellValue VisitRefError(RefErrorExpr node) =>
        CellValue.FromError(CellError.Reference("A referenced cell was deleted."));

    public CellValue VisitUnary(UnaryExpr node)
    {
        var operand = Evaluate(node.Operand);
        if (operand.IsError)
        {
            return operand;
        }

        if (!Coercion.TryToNumber(operand, out double value, out var error))
        {
            return CellValue.FromError(error!);
        }

        return node.Operator switch
        {
            UnaryOperator.Negate => CellValue.Number(-value),
            UnaryOperator.Plus => CellValue.Number(value),
            UnaryOperator.Percent => CellValue.Number(value / 100d),
            _ => CellValue.FromError(CellError.Value()),
        };
    }

    public CellValue VisitBinary(BinaryExpr node)
    {
        var left = Evaluate(node.Left);
        var right = Evaluate(node.Right);

        return node.Operator switch
        {
            BinaryOperator.Add => Arithmetic(left, right, static (a, b) => a + b),
            BinaryOperator.Subtract => Arithmetic(left, right, static (a, b) => a - b),
            BinaryOperator.Multiply => Arithmetic(left, right, static (a, b) => a * b),
            BinaryOperator.Divide => Divide(left, right),
            BinaryOperator.Power => Power(left, right),
            BinaryOperator.Concat => Concat(left, right),
            BinaryOperator.Equal => ValueComparer.Equal(left, right),
            BinaryOperator.NotEqual => ValueComparer.NotEqual(left, right),
            BinaryOperator.Less => ValueComparer.Less(left, right),
            BinaryOperator.LessEqual => ValueComparer.LessEqual(left, right),
            BinaryOperator.Greater => ValueComparer.Greater(left, right),
            BinaryOperator.GreaterEqual => ValueComparer.GreaterEqual(left, right),
            _ => CellValue.FromError(CellError.Value()),
        };
    }

    public CellValue VisitFunctionCall(FunctionCallExpr node)
    {
        if (!_functions.TryGet(node.Name, out var function))
        {
            return CellValue.FromError(CellError.Name($"Unknown function '{node.Name}'."));
        }

        if (!function.Metadata.AcceptsArgCount(node.Arguments.Count))
        {
            return CellValue.FromError(CellError.Value(
                $"{node.Name} does not accept {node.Arguments.Count} argument(s)."));
        }

        return function.Invoke(node.Arguments, this);
    }

    // ---- IFunctionEvaluator ----------------------------------------------

    public IEnumerable<CellValue> EnumerateValues(Expr expression)
    {
        if (TryEvaluateRange(expression, out var range) && expression is not ReferenceExpr)
        {
            return range.Values();
        }

        return [Evaluate(expression)];
    }

    public bool TryEvaluateRange(Expr expression, out RangeValues range)
    {
        switch (expression)
        {
            case RangeExpr rangeExpr:
                range = new RangeValues(rangeExpr.Range, Context);
                return true;
            case ReferenceExpr referenceExpr:
                range = new RangeValues(new CellRange(referenceExpr.Address), Context);
                return true;
            case NameExpr nameExpr when Context.TryResolveName(nameExpr.Name, out var named):
                range = new RangeValues(named, Context);
                return true;
            default:
                range = null!;
                return false;
        }
    }

    // ---- Arithmetic helpers ----------------------------------------------

    private static CellValue Arithmetic(CellValue left, CellValue right, Func<double, double, double> op)
    {
        if (left.IsError)
        {
            return left;
        }

        if (right.IsError)
        {
            return right;
        }

        if (!Coercion.TryToNumber(left, out double a, out var ea))
        {
            return CellValue.FromError(ea!);
        }

        if (!Coercion.TryToNumber(right, out double b, out var eb))
        {
            return CellValue.FromError(eb!);
        }

        return Finite(op(a, b));
    }

    private static CellValue Divide(CellValue left, CellValue right)
    {
        if (left.IsError)
        {
            return left;
        }

        if (right.IsError)
        {
            return right;
        }

        if (!Coercion.TryToNumber(left, out double a, out var ea))
        {
            return CellValue.FromError(ea!);
        }

        if (!Coercion.TryToNumber(right, out double b, out var eb))
        {
            return CellValue.FromError(eb!);
        }

        if (b == 0d)
        {
            return CellValue.FromError(CellError.DivideByZero());
        }

        return Finite(a / b);
    }

    private static CellValue Power(CellValue left, CellValue right)
    {
        if (left.IsError)
        {
            return left;
        }

        if (right.IsError)
        {
            return right;
        }

        if (!Coercion.TryToNumber(left, out double a, out var ea))
        {
            return CellValue.FromError(ea!);
        }

        if (!Coercion.TryToNumber(right, out double b, out var eb))
        {
            return CellValue.FromError(eb!);
        }

        return Finite(Math.Pow(a, b));
    }

    private static CellValue Concat(CellValue left, CellValue right)
    {
        if (!Coercion.TryToText(left, out string a, out var ea))
        {
            return CellValue.FromError(ea!);
        }

        if (!Coercion.TryToText(right, out string b, out var eb))
        {
            return CellValue.FromError(eb!);
        }

        return CellValue.Text(a + b);
    }

    /// <summary>Guard against non-finite results; Cellestra has no #NUM!, so these surface as #VALUE!.</summary>
    private static CellValue Finite(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? CellValue.FromError(CellError.Value("The result is not a finite number."))
            : CellValue.Number(value);
}
