using System.Globalization;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Data;

/// <summary>
/// Batch data-validation check for bulk writes (paste, fill). Type-in validation is enforced one
/// cell at a time in the grid; this is the pure, testable analog that, given the cells a write would
/// produce, returns those that would violate a <see cref="ValidationResponse.Stop"/> rule. The UI
/// runs it before executing the paste/fill command and aborts the whole operation on any violation
/// (PRD §8.13). Mirrors the grid's per-cell rule: formulas are skipped (validated on recalc), empty
/// values pass, and only Stop rules block (Warning/Information are advisory).
/// </summary>
public static class ValidationPreflight
{
    public static IReadOnlyList<(int Row, int Column)> FindStopViolations(
        Worksheet sheet,
        IEnumerable<(int Row, int Column, CellValue Value, bool IsFormula)> incoming,
        CultureInfo culture,
        Func<CellRange, IEnumerable<CellValue>>? resolveRange = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(incoming);

        var violations = new List<(int, int)>();
        foreach (var (row, column, value, isFormula) in incoming)
        {
            if (isFormula)
            {
                continue; // formulas are validated on recalc, not on entry
            }

            var rule = sheet.FindValidation(row, column);
            if (rule is null || rule.Response != ValidationResponse.Stop)
            {
                continue;
            }

            if (!Validator.IsValid(rule, value, culture, resolveRange))
            {
                violations.Add((row, column));
            }
        }

        return violations;
    }
}
