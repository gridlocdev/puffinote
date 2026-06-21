using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions.Logical;

/// <summary>The v1 logical functions (PRD §8.2).</summary>
public static class LogicalFunctions
{
    public static IEnumerable<IFunction> All()
    {
        yield return new DelegateFunction(
            H.Meta("IF", FunctionCategory.Logical, "Returns one value if a condition is true, another if false.",
                ArgumentKind.Any, "IF(A1>0, \"yes\", \"no\")",
                H.Bln("condition"), H.Val("value_if_true"), H.Val("value_if_false", required: false)),
            (args, ev) =>
            {
                if (!H.TryBool(ev, args[0], out bool condition, out var error))
                {
                    return error;
                }

                if (condition)
                {
                    return ev.Evaluate(args[1]);
                }

                return args.Count > 2 ? ev.Evaluate(args[2]) : CellValue.Boolean(false);
            });

        yield return new DelegateFunction(
            H.Meta("IFERROR", FunctionCategory.Logical, "Returns a fallback value when an expression is an error.",
                ArgumentKind.Any, "IFERROR(A1/B1, 0)", H.Val("value"), H.Val("value_if_error")),
            (args, ev) =>
            {
                var value = ev.Evaluate(args[0]);
                return value.IsError ? ev.Evaluate(args[1]) : value;
            });

        yield return new DelegateFunction(
            H.Meta("AND", FunctionCategory.Logical, "TRUE if every argument is true.", ArgumentKind.Boolean,
                "AND(A1>0, B1>0)", H.Bln("logical", repeatable: true)),
            (args, ev) => Reduce(args, ev, requireAll: true));

        yield return new DelegateFunction(
            H.Meta("OR", FunctionCategory.Logical, "TRUE if any argument is true.", ArgumentKind.Boolean,
                "OR(A1>0, B1>0)", H.Bln("logical", repeatable: true)),
            (args, ev) => Reduce(args, ev, requireAll: false));

        yield return new DelegateFunction(
            H.Meta("NOT", FunctionCategory.Logical, "Reverses a logical value.", ArgumentKind.Boolean,
                "NOT(A1)", H.Bln("logical")),
            (args, ev) => H.TryBool(ev, args[0], out bool b, out var error)
                ? CellValue.Boolean(!b)
                : error);

        yield return new DelegateFunction(
            H.Meta("TRUE", FunctionCategory.Logical, "Returns the logical value TRUE.", ArgumentKind.Boolean, "TRUE()"),
            (_, _) => CellValue.Boolean(true));

        yield return new DelegateFunction(
            H.Meta("FALSE", FunctionCategory.Logical, "Returns the logical value FALSE.", ArgumentKind.Boolean, "FALSE()"),
            (_, _) => CellValue.Boolean(false));
    }

    private static CellValue Reduce(IReadOnlyList<Expr> args, IFunctionEvaluator ev, bool requireAll)
    {
        bool any = false;
        bool accumulator = requireAll;
        foreach (var arg in args)
        {
            bool fromReference = ev.TryEvaluateRange(arg, out _);
            foreach (var value in ev.EnumerateValues(arg))
            {
                if (value.IsError)
                {
                    return value;
                }

                if (value.IsEmpty)
                {
                    continue;
                }

                if (!Coercion.TryToBoolean(value, out bool b, out var coercionError))
                {
                    if (fromReference)
                    {
                        continue; // non-logical text in a range is ignored, per Calc
                    }

                    return CellValue.FromError(coercionError!); // a direct non-logical argument errors
                }

                any = true;
                accumulator = requireAll ? accumulator && b : accumulator || b;
            }
        }

        return any
            ? CellValue.Boolean(accumulator)
            : CellValue.FromError(CellError.Value("No logical values were supplied."));
    }
}
