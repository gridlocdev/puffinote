using System.Globalization;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Tests.Formulas;

/// <summary>Helpers to evaluate a formula string against an in-memory workbook in tests.</summary>
internal static class FormulaHarness
{
    private static readonly FunctionRegistry Registry = FunctionRegistry.CreateDefault();

    public static CellValue Eval(string formula, Action<Worksheet>? setup = null, DateTime? now = null, CultureInfo? culture = null)
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        setup?.Invoke(sheet);

        var context = new WorkbookEvaluationContext(
            workbook,
            sheet.Name,
            culture ?? CultureInfo.InvariantCulture,
            now is null ? null : () => now.Value);

        var evaluator = new Evaluator(context, Registry);
        var expr = Parser.Parse(formula);
        return evaluator.Evaluate(expr);
    }

    public static double Number(string formula, Action<Worksheet>? setup = null, DateTime? now = null)
    {
        var value = Eval(formula, setup, now);
        if (value.IsError)
        {
            throw new Xunit.Sdk.XunitException($"Expected a number from '{formula}' but got {value.AsError.Token}: {value.AsError.Reason}");
        }

        return value.AsNumber;
    }

    public static string Text(string formula, Action<Worksheet>? setup = null) => Eval(formula, setup).AsText;

    public static string ErrorToken(string formula, Action<Worksheet>? setup = null) => Eval(formula, setup).AsError.Token;
}
