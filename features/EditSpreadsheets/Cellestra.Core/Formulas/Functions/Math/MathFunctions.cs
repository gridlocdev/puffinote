using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions.Math;

/// <summary>The v1 math &amp; statistics functions (PRD §8.2).</summary>
public static class MathFunctions
{
    public static IEnumerable<IFunction> All()
    {
        yield return Aggregate("SUM", FunctionCategory.Math, "Adds all the numbers in a range or list.", "SUM(A1:A10)",
            nums => nums.Sum());

        yield return Aggregate("PRODUCT", FunctionCategory.Math, "Multiplies all the numbers given.", "PRODUCT(A1:A5)",
            nums => nums.Count == 0 ? 0d : nums.Aggregate(1d, (acc, n) => acc * n));

        yield return new DelegateFunction(
            H.Meta("AVERAGE", FunctionCategory.Statistical, "Returns the average (arithmetic mean) of the numbers.",
                ArgumentKind.Number, "AVERAGE(A1:A10)", H.Num("number", repeatable: true)),
            (args, ev) =>
            {
                var nums = new List<double>();
                var error = H.CollectNumbers(ev, args, nums);
                if (error.HasValue)
                {
                    return error.Value;
                }

                return nums.Count == 0
                    ? CellValue.FromError(CellError.DivideByZero("AVERAGE has no numeric values."))
                    : CellValue.Number(nums.Average());
            });

        yield return Aggregate("MIN", FunctionCategory.Statistical, "Returns the smallest number.", "MIN(A1:A10)",
            nums => nums.Count == 0 ? 0d : nums.Min());

        yield return Aggregate("MAX", FunctionCategory.Statistical, "Returns the largest number.", "MAX(A1:A10)",
            nums => nums.Count == 0 ? 0d : nums.Max());

        yield return new DelegateFunction(
            H.Meta("COUNT", FunctionCategory.Statistical, "Counts how many cells contain numbers.",
                ArgumentKind.Number, "COUNT(A1:A10)", H.Val("value", repeatable: true)),
            (args, ev) => CellValue.Number(CountValues(args, ev, onlyNumbers: true)));

        yield return new DelegateFunction(
            H.Meta("COUNTA", FunctionCategory.Statistical, "Counts how many cells are not empty.",
                ArgumentKind.Number, "COUNTA(A1:A10)", H.Val("value", repeatable: true)),
            (args, ev) => CellValue.Number(CountValues(args, ev, onlyNumbers: false)));

        yield return Unary("ABS", "Returns the absolute value of a number.", "ABS(-5)", System.Math.Abs);
        yield return Unary("INT", "Rounds a number down to the nearest integer.", "INT(3.7)", System.Math.Floor);

        yield return new DelegateFunction(
            H.Meta("SQRT", FunctionCategory.Math, "Returns the positive square root of a number.",
                ArgumentKind.Number, "SQRT(16)", H.Num("number")),
            (args, ev) =>
            {
                if (!H.TryNumber(ev, args[0], out double n, out var err))
                {
                    return err;
                }

                return n < 0
                    ? CellValue.FromError(CellError.Value("SQRT of a negative number."))
                    : CellValue.Number(System.Math.Sqrt(n));
            });

        yield return new DelegateFunction(
            H.Meta("POWER", FunctionCategory.Math, "Raises a number to a power.", ArgumentKind.Number,
                "POWER(2, 10)", H.Num("base"), H.Num("exponent")),
            (args, ev) =>
            {
                if (!H.TryNumber(ev, args[0], out double a, out var ea))
                {
                    return ea;
                }

                if (!H.TryNumber(ev, args[1], out double b, out var eb))
                {
                    return eb;
                }

                double result = System.Math.Pow(a, b);
                return double.IsNaN(result) || double.IsInfinity(result)
                    ? CellValue.FromError(CellError.Value("POWER result is not a finite number."))
                    : CellValue.Number(result);
            });

        yield return new DelegateFunction(
            H.Meta("MOD", FunctionCategory.Math, "Returns the remainder after division (sign follows the divisor).",
                ArgumentKind.Number, "MOD(10, 3)", H.Num("number"), H.Num("divisor")),
            (args, ev) =>
            {
                if (!H.TryNumber(ev, args[0], out double n, out var en))
                {
                    return en;
                }

                if (!H.TryNumber(ev, args[1], out double d, out var ed))
                {
                    return ed;
                }

                if (d == 0d)
                {
                    return CellValue.FromError(CellError.DivideByZero("MOD by zero."));
                }

                // Excel/Calc MOD: result takes the sign of the divisor.
                return CellValue.Number(n - (d * System.Math.Floor(n / d)));
            });

        yield return Rounding("ROUND", "Rounds a number to a given number of digits (half away from zero).",
            "ROUND(3.14159, 2)", (value, digits) => H.RoundHalfAway(value, digits));

        yield return Rounding("ROUNDUP", "Rounds a number away from zero.", "ROUNDUP(3.14159, 2)",
            (value, digits) => Directional(value, digits, away: true));

        yield return Rounding("ROUNDDOWN", "Rounds a number toward zero.", "ROUNDDOWN(3.14159, 2)",
            (value, digits) => Directional(value, digits, away: false));

        yield return new DelegateFunction(
            H.Meta("SUMIF", FunctionCategory.Math, "Adds the cells that meet a criteria.", ArgumentKind.Number,
                "SUMIF(A1:A10, \">5\")", H.Rng("range"), H.Val("criteria"), H.Rng("sum_range", required: false)),
            (args, ev) => SumOrCountIf(args, ev, sum: true));

        yield return new DelegateFunction(
            H.Meta("COUNTIF", FunctionCategory.Statistical, "Counts the cells that meet a criteria.",
                ArgumentKind.Number, "COUNTIF(A1:A10, \">5\")", H.Rng("range"), H.Val("criteria")),
            (args, ev) => SumOrCountIf(args, ev, sum: false));
    }

    private static IFunction Aggregate(string name, FunctionCategory category, string summary, string example, Func<List<double>, double> reduce) =>
        new DelegateFunction(
            H.Meta(name, category, summary, ArgumentKind.Number, example, H.Num("number", repeatable: true)),
            (args, ev) =>
            {
                var nums = new List<double>();
                var error = H.CollectNumbers(ev, args, nums);
                return error ?? CellValue.Number(reduce(nums));
            });

    private static IFunction Unary(string name, string summary, string example, Func<double, double> op) =>
        new DelegateFunction(
            H.Meta(name, FunctionCategory.Math, summary, ArgumentKind.Number, example, H.Num("number")),
            (args, ev) => H.TryNumber(ev, args[0], out double n, out var err) ? CellValue.Number(op(n)) : err);

    private static IFunction Rounding(string name, string summary, string example, Func<double, int, double> round) =>
        new DelegateFunction(
            H.Meta(name, FunctionCategory.Math, summary, ArgumentKind.Number, example,
                H.Num("number"), H.Num("digits", required: false)),
            (args, ev) =>
            {
                if (!H.TryNumber(ev, args[0], out double value, out var ev1))
                {
                    return ev1;
                }

                int digits = 0;
                if (args.Count > 1 && !H.TryInt(ev, args[1], out digits, out var ev2))
                {
                    return ev2;
                }

                return CellValue.Number(round(value, digits));
            });

    private static double Directional(double value, int digits, bool away)
    {
        double factor = System.Math.Pow(10, digits);
        double scaled = value * factor;
        double adjusted = away ? AwayFromZero(scaled) : TowardZero(scaled);
        return adjusted / factor;
    }

    private static double AwayFromZero(double x) => x < 0 ? System.Math.Floor(x) : System.Math.Ceiling(x);

    private static double TowardZero(double x) => System.Math.Truncate(x);

    private static double CountValues(IReadOnlyList<Expr> args, IFunctionEvaluator ev, bool onlyNumbers)
    {
        int count = 0;
        foreach (var arg in args)
        {
            // COUNT counts numbers from references, but a value typed directly as an argument also
            // counts when it is a boolean or a numeric string — COUNT("x",1,TRUE)=2 (Excel/Calc).
            bool fromReference = ev.TryEvaluateRange(arg, out _);
            foreach (var value in ev.EnumerateValues(arg))
            {
                if (!onlyNumbers)
                {
                    if (!value.IsEmpty)
                    {
                        count++;
                    }
                }
                else if (value.IsNumber)
                {
                    count++;
                }
                else if (!fromReference && !value.IsEmpty &&
                    Evaluation.Coercion.TryToNumber(value, out _, out _))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static CellValue SumOrCountIf(IReadOnlyList<Expr> args, IFunctionEvaluator ev, bool sum)
    {
        if (!ev.TryEvaluateRange(args[0], out var range))
        {
            return CellValue.FromError(CellError.Value("The first argument must be a range."));
        }

        var criteriaValue = ev.Evaluate(args[1]);
        if (criteriaValue.IsError)
        {
            return criteriaValue;
        }

        var matcher = CriteriaMatcher.Build(criteriaValue, ev.Context.Culture);

        RangeValues? sumRange = null;
        if (sum && args.Count > 2)
        {
            if (!ev.TryEvaluateRange(args[2], out var resolved))
            {
                return CellValue.FromError(CellError.Value("The sum range must be a range."));
            }

            sumRange = resolved;
        }

        double total = 0;
        int count = 0;
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColumnCount; c++)
            {
                var cell = range.Get(r, c);
                if (cell.IsError)
                {
                    return cell;
                }

                if (!matcher.Matches(cell))
                {
                    continue;
                }

                count++;
                if (!sum)
                {
                    continue;
                }

                // Excel/Calc anchor sum_range at its top-left and reshape it to the criteria
                // range's dimensions, so sum_range's own size/shape is ignored. Read the cell at
                // offset (r, c) from sum_range's top-left via the context rather than bounding by
                // sum_range's own RowCount/ColumnCount (which would yield #REF! on a mismatch).
                var target = sumRange is null
                    ? cell
                    : ev.Context.GetCell(sumRange.Range.Sheet, sumRange.Range.Top + r, sumRange.Range.Left + c);
                if (target.IsError)
                {
                    return target;
                }

                if (target.IsNumber)
                {
                    total += target.AsNumber;
                }
            }
        }

        return CellValue.Number(sum ? total : count);
    }
}
