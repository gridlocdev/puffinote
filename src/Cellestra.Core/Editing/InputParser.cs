using System.Globalization;
using Cellestra.Core.Values;

namespace Cellestra.Core.Editing;

/// <summary>
/// Interprets what a user typed into a cell: a formula (leading <c>=</c>), a boolean, a number
/// (parsed in the active locale), or text. This is the single place raw input becomes a value
/// or formula (PRD §5 "Edit a value").
/// </summary>
public static class InputParser
{
    public readonly record struct ParsedInput(string? Formula, CellValue Value)
    {
        public bool IsFormula => Formula is not null;
    }

    public static ParsedInput Parse(string text, CultureInfo culture)
    {
        if (text is null || text.Length == 0)
        {
            return new ParsedInput(null, CellValue.Empty);
        }

        if (text[0] == '=' && text.Length > 1)
        {
            return new ParsedInput(text[1..], CellValue.Empty);
        }

        if (string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedInput(null, CellValue.Boolean(true));
        }

        if (string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedInput(null, CellValue.Boolean(false));
        }

        if (double.TryParse(text, NumberStyles.Any, culture, out double number))
        {
            return new ParsedInput(null, CellValue.Number(number));
        }

        return new ParsedInput(null, CellValue.Text(text));
    }
}
