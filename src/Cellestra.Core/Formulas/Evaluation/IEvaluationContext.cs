using System.Globalization;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Evaluation;

/// <summary>
/// Everything the evaluator needs from the workbook to compute a formula: cell lookup
/// (across sheets), named-range resolution, the active locale, and a clock for the
/// volatile date/time functions. Implemented over a live <see cref="Workbook"/> at the
/// recalc layer; tests provide a lightweight fake.
/// </summary>
public interface IEvaluationContext
{
    /// <summary>The sheet a formula lives on; unqualified references resolve here.</summary>
    string CurrentSheet { get; }

    /// <summary>Locale for VALUE/TEXT/DATE parsing and formatting (PRD §8.3).</summary>
    CultureInfo Culture { get; }

    /// <summary>Current date/time for the volatile TODAY/NOW functions (injectable for tests).</summary>
    DateTime Now { get; }

    /// <summary>The displayed value of a cell. <paramref name="sheet"/> null means <see cref="CurrentSheet"/>.</summary>
    CellValue GetCell(string? sheet, int row, int column);

    /// <summary>Resolve a defined name to its range. Returns false for an unknown name.</summary>
    bool TryResolveName(string name, out CellRange range);
}
