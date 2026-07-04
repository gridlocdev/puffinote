using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions;

/// <summary>
/// Builds a predicate from a SUMIF/COUNTIF criteria value: a comparison (<c>"&gt;5"</c>,
/// <c>"&lt;&gt;0"</c>), a number/text equality, or a text pattern with <c>*</c>/<c>?</c>
/// wildcards. Comparisons against numbers use numeric ordering; text matches are
/// case-insensitive.
/// </summary>
public sealed class CriteriaMatcher
{
    private readonly Func<CellValue, bool> _predicate;

    private CriteriaMatcher(Func<CellValue, bool> predicate) => _predicate = predicate;

    public bool Matches(CellValue value) => _predicate(value);

    public static CriteriaMatcher Build(CellValue criteria, CultureInfo culture)
    {
        if (criteria.IsNumber)
        {
            double target = criteria.AsNumber;
            return new CriteriaMatcher(v => v.IsNumber && v.AsNumber == target);
        }

        if (criteria.IsBoolean)
        {
            bool target = criteria.AsBoolean;
            return new CriteriaMatcher(v => v.IsBoolean && v.AsBoolean == target);
        }

        string text = criteria.IsText ? criteria.AsText : criteria.ToInvariantString();
        (string op, string operand) = SplitOperator(text);

        // Comparison against a number, when the operand is numeric.
        if (op is "=" or "<>" or ">" or ">=" or "<" or "<=" &&
            double.TryParse(operand, NumberStyles.Any, culture, out double numericOperand))
        {
            return new CriteriaMatcher(v => CompareNumeric(v, op, numericOperand));
        }

        // Text comparison / equality (with wildcards for = and <>).
        return op switch
        {
            ">" or ">=" or "<" or "<=" => new CriteriaMatcher(v => CompareText(v, op, operand)),
            "<>" => new CriteriaMatcher(v => !TextMatches(v, operand)),
            _ => new CriteriaMatcher(v => TextMatches(v, operand)),
        };
    }

    private static (string Operator, string Operand) SplitOperator(string text)
    {
        foreach (string op in new[] { "<=", ">=", "<>", "=", "<", ">" })
        {
            if (text.StartsWith(op, StringComparison.Ordinal))
            {
                return (op, text[op.Length..]);
            }
        }

        return ("=", text);
    }

    private static bool CompareNumeric(CellValue value, string op, double operand)
    {
        if (!value.IsNumber)
        {
            return op == "<>";
        }

        double v = value.AsNumber;
        return op switch
        {
            "=" => v == operand,
            "<>" => v != operand,
            ">" => v > operand,
            ">=" => v >= operand,
            "<" => v < operand,
            "<=" => v <= operand,
            _ => false,
        };
    }

    private static bool CompareText(CellValue value, string op, string operand)
    {
        string v = value.IsText ? value.AsText : value.ToInvariantString();
        int cmp = string.Compare(v, operand, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            ">" => cmp > 0,
            ">=" => cmp >= 0,
            "<" => cmp < 0,
            "<=" => cmp <= 0,
            _ => false,
        };
    }

    private static bool TextMatches(CellValue value, string pattern)
    {
        string v = value.IsText ? value.AsText : value.ToInvariantString();
        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
        {
            return string.Equals(v, pattern, StringComparison.OrdinalIgnoreCase);
        }

        return Regex.IsMatch(v, WildcardToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string WildcardToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (char c in pattern)
        {
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    break;
                case '?':
                    sb.Append('.');
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }
}
