using System.Globalization;
using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions;

/// <summary>
/// Shared argument-reading and aggregation helpers used by the built-in functions.
/// Each <c>Try*</c> returns false and sets <paramref name="error"/> to a #-error value
/// the function should return directly; otherwise it yields the coerced argument.
/// </summary>
internal static class H
{
    public static bool TryNumber(IFunctionEvaluator ev, Expr arg, out double number, out CellValue error)
    {
        var value = ev.Evaluate(arg);
        if (value.IsError)
        {
            number = 0;
            error = value;
            return false;
        }

        if (!Coercion.TryToNumber(value, out number, out var coercionError))
        {
            error = CellValue.FromError(coercionError!);
            return false;
        }

        error = CellValue.Empty;
        return true;
    }

    public static bool TryInt(IFunctionEvaluator ev, Expr arg, out int result, out CellValue error)
    {
        if (!TryNumber(ev, arg, out double number, out error))
        {
            result = 0;
            return false;
        }

        result = (int)System.Math.Truncate(number);
        return true;
    }

    public static bool TryText(IFunctionEvaluator ev, Expr arg, out string text, out CellValue error)
    {
        var value = ev.Evaluate(arg);
        if (value.IsError)
        {
            text = string.Empty;
            error = value;
            return false;
        }

        if (!Coercion.TryToText(value, out text, out var coercionError))
        {
            error = CellValue.FromError(coercionError!);
            return false;
        }

        error = CellValue.Empty;
        return true;
    }

    public static bool TryBool(IFunctionEvaluator ev, Expr arg, out bool result, out CellValue error)
    {
        var value = ev.Evaluate(arg);
        if (value.IsError)
        {
            result = false;
            error = value;
            return false;
        }

        if (!Coercion.TryToBoolean(value, out result, out var coercionError))
        {
            error = CellValue.FromError(coercionError!);
            return false;
        }

        error = CellValue.Empty;
        return true;
    }

    /// <summary>
    /// Collect numeric values from a list of arguments (flattening ranges). Values pulled from a
    /// reference/range follow the LibreOffice rule — only numbers are added, blanks/text/booleans
    /// ignored — but a value typed <i>directly</i> as an argument is coerced like arithmetic, so
    /// SUM(TRUE,FALSE,5)=6 and SUM(1,"x")=#VALUE!. Returns an error value to propagate, else null.
    /// </summary>
    public static CellValue? CollectNumbers(IFunctionEvaluator ev, IReadOnlyList<Expr> args, List<double> destination)
    {
        foreach (var arg in args)
        {
            bool fromReference = ev.TryEvaluateRange(arg, out _);
            foreach (var value in ev.EnumerateValues(arg))
            {
                if (value.IsError)
                {
                    return value;
                }

                if (fromReference)
                {
                    if (value.IsNumber)
                    {
                        destination.Add(value.AsNumber);
                    }
                }
                else if (!Coercion.TryToNumber(value, out double number, out var coercionError))
                {
                    return CellValue.FromError(coercionError!);
                }
                else
                {
                    destination.Add(number);
                }
            }
        }

        return null;
    }

    /// <summary>Round half away from zero (Excel/Calc ROUND semantics) to the given decimal places.</summary>
    public static double RoundHalfAway(double value, int digits)
    {
        double factor = System.Math.Pow(10, digits);
        double scaled = value * factor;
        double rounded = System.Math.Round(scaled, MidpointRounding.AwayFromZero);
        return rounded / factor;
    }

    /// <summary>Parse a value (text or number) for VALUE() and lookups, using the active locale.</summary>
    public static bool TryParseNumberOrDate(string text, CultureInfo culture, out double number)
    {
        text = text.Trim();

        // A trailing percent sign means "this fraction of one": "50%" → 0.5 (Excel/Calc).
        if (text.EndsWith('%'))
        {
            string body = text[..^1].Trim();
            if (double.TryParse(body, NumberStyles.Any, culture, out number) ||
                double.TryParse(body, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                number /= 100d;
                return true;
            }
        }

        if (double.TryParse(text, NumberStyles.Any, culture, out number))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        // A leading/trailing currency symbol (the culture's, or a plain "$") is stripped so
        // "$100" parses as 100. NumberStyles.Any only honours the *current* culture's symbol,
        // so handle the common "$" explicitly for invariant/foreign locales.
        string stripped = text;
        if (stripped.StartsWith('$'))
        {
            stripped = stripped[1..].TrimStart();
        }
        else if (stripped.StartsWith(culture.NumberFormat.CurrencySymbol, StringComparison.Ordinal))
        {
            stripped = stripped[culture.NumberFormat.CurrencySymbol.Length..].TrimStart();
        }

        if (!ReferenceEquals(stripped, text) &&
            (double.TryParse(stripped, NumberStyles.Any, culture, out number) ||
             double.TryParse(stripped, NumberStyles.Any, CultureInfo.InvariantCulture, out number)))
        {
            return true;
        }

        if (System.DateTime.TryParse(text, culture, DateTimeStyles.None, out var date))
        {
            number = DateConversion.ToSerial(date);
            return true;
        }

        number = 0;
        return false;
    }

    /// <summary>Quick metadata constructor with positional required arguments of a single kind.</summary>
    public static FunctionMetadata Meta(
        string name,
        FunctionCategory category,
        string summary,
        ArgumentKind returnKind,
        string example,
        params FunctionArgument[] arguments) =>
        new(name, category, summary, arguments, returnKind, example);

    public static FunctionArgument Arg(string name, ArgumentKind kind, bool required = true, bool repeatable = false) =>
        new(name, kind, required, repeatable);

    public static FunctionArgument Num(string name, bool required = true, bool repeatable = false) =>
        new(name, ArgumentKind.Number, required, repeatable);

    public static FunctionArgument Txt(string name, bool required = true, bool repeatable = false) =>
        new(name, ArgumentKind.Text, required, repeatable);

    public static FunctionArgument Rng(string name, bool required = true, bool repeatable = false) =>
        new(name, ArgumentKind.Range, required, repeatable);

    public static FunctionArgument Val(string name, bool required = true, bool repeatable = false) =>
        new(name, ArgumentKind.Any, required, repeatable);

    public static FunctionArgument Bln(string name, bool required = true, bool repeatable = false) =>
        new(name, ArgumentKind.Boolean, required, repeatable);
}
