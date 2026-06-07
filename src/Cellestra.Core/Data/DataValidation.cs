using System.Globalization;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Data;

/// <summary>The kind of constraint a validation rule enforces (PRD §8.13).</summary>
public enum ValidationKind
{
    Any,
    List,
    WholeNumber,
    Decimal,
    Date,
    TextLength,
}

public enum ValidationOperator
{
    Between,
    NotBetween,
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
}

/// <summary>How invalid input is handled. Stop is the default (PRD §8.13).</summary>
public enum ValidationResponse
{
    Stop,
    Warning,
    Information,
}

/// <summary>
/// A data-validation rule bound to a range (PRD §8.13): a list (literal values or a range),
/// or a number/date/text-length constraint. Maps to ODF <c>table:content-validation</c>.
/// Checks input on entry; it does not flag values already present.
/// </summary>
public sealed record DataValidation(ValidationKind Kind, CellRange Target)
{
    public ValidationOperator Operator { get; init; } = ValidationOperator.Between;

    public double Operand1 { get; init; }

    public double Operand2 { get; init; }

    public IReadOnlyList<string>? ListValues { get; init; }

    public CellRange? ListSource { get; init; }

    public string? InputMessage { get; init; }

    public string? ErrorMessage { get; init; }

    public ValidationResponse Response { get; init; } = ValidationResponse.Stop;

    public bool Covers(int row, int column) => Target.Contains(row, column);
}

/// <summary>Checks a candidate value against a <see cref="DataValidation"/> rule.</summary>
public static class Validator
{
    public static bool IsValid(DataValidation rule, CellValue value, CultureInfo culture, Func<CellRange, IEnumerable<CellValue>>? resolveRange = null)
    {
        if (value.IsEmpty)
        {
            return true; // empty is allowed; required-ness is separate
        }

        return rule.Kind switch
        {
            ValidationKind.Any => true,
            ValidationKind.List => MatchesList(rule, value, culture, resolveRange),
            ValidationKind.TextLength => CompareNumber(rule, value.IsText ? value.AsText.Length : value.ToInvariantString().Length),
            ValidationKind.WholeNumber => IsNumber(value, out double whole) && whole == Math.Truncate(whole) && CompareNumber(rule, whole),
            ValidationKind.Decimal => IsNumber(value, out double dec) && CompareNumber(rule, dec),
            ValidationKind.Date => IsNumber(value, out double date) && CompareNumber(rule, date),
            _ => true,
        };
    }

    private static bool MatchesList(DataValidation rule, CellValue value, CultureInfo culture, Func<CellRange, IEnumerable<CellValue>>? resolveRange)
    {
        string text = value.IsText ? value.AsText : NumberFormatterToText(value, culture);

        if (rule.ListValues is not null &&
            rule.ListValues.Any(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (rule.ListSource is { } source && resolveRange is not null)
        {
            foreach (var candidate in resolveRange(source))
            {
                string candidateText = candidate.IsText ? candidate.AsText : candidate.ToInvariantString();
                if (string.Equals(candidateText, text, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CompareNumber(DataValidation rule, double v) => rule.Operator switch
    {
        ValidationOperator.Between => v >= rule.Operand1 && v <= rule.Operand2,
        ValidationOperator.NotBetween => v < rule.Operand1 || v > rule.Operand2,
        ValidationOperator.Equal => v == rule.Operand1,
        ValidationOperator.NotEqual => v != rule.Operand1,
        ValidationOperator.GreaterThan => v > rule.Operand1,
        ValidationOperator.LessThan => v < rule.Operand1,
        ValidationOperator.GreaterOrEqual => v >= rule.Operand1,
        ValidationOperator.LessOrEqual => v <= rule.Operand1,
        _ => true,
    };

    private static bool IsNumber(CellValue value, out double number)
    {
        if (value.IsNumber || value.IsBoolean)
        {
            number = value.AsNumber;
            return true;
        }

        number = 0;
        return false;
    }

    private static string NumberFormatterToText(CellValue value, CultureInfo culture) =>
        value.IsBoolean ? (value.AsBoolean ? "TRUE" : "FALSE") : value.ToInvariantString();
}
