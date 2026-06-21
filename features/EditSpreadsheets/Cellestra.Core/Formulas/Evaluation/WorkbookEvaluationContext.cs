using System.Globalization;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Evaluation;

/// <summary>
/// An <see cref="IEvaluationContext"/> backed by a live <see cref="Workbook"/>. Resolves
/// unqualified references against <see cref="CurrentSheet"/> and cross-sheet references by
/// name. The clock and culture are supplied so the recalc layer (and tests) stay deterministic.
/// </summary>
public sealed class WorkbookEvaluationContext : IEvaluationContext
{
    private readonly Workbook _workbook;
    private readonly Func<DateTime> _clock;

    public WorkbookEvaluationContext(Workbook workbook, string currentSheet, CultureInfo? culture = null, Func<DateTime>? clock = null)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        CurrentSheet = currentSheet ?? throw new ArgumentNullException(nameof(currentSheet));
        Culture = culture ?? workbook.Settings.EffectiveLocale;
        _clock = clock ?? (() => DateTime.Now);
    }

    public string CurrentSheet { get; }

    public CultureInfo Culture { get; }

    public DateTime Now => _clock();

    public CellValue GetCell(string? sheet, int row, int column)
    {
        var worksheet = sheet is null
            ? _workbook.FindSheet(CurrentSheet)
            : _workbook.FindSheet(sheet);

        if (worksheet is null)
        {
            return CellValue.FromError(CellError.Reference($"Unknown sheet '{sheet}'."));
        }

        if (!SheetDimensions.IsValidRow(row) || !SheetDimensions.IsValidColumn(column))
        {
            return CellValue.FromError(CellError.Reference("Reference is outside the sheet."));
        }

        return worksheet.GetValue(row, column);
    }

    public bool TryResolveName(string name, out CellRange range)
    {
        var named = _workbook.FindNamedRange(name, CurrentSheet);
        if (named is null)
        {
            range = default;
            return false;
        }

        range = named.Target;
        return true;
    }
}
