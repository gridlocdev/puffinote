using Cellestra.Core.Model;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>
/// Builds and parses <c>content.xml</c> — the cell grid, automatic styles, merges, column
/// widths/row heights, hidden lines, and named ranges. Formulas go through
/// <see cref="OpenFormulaTranslator"/>. Unmodeled <c>office:spreadsheet</c> children are
/// captured/re-emitted so nothing is silently dropped (PRD §8.1).
/// </summary>
/// <remarks>
/// Split across concern partials: <c>.Write</c> (grid/cell serialization), <c>.Read</c> (parsing),
/// <c>.Validation</c> (<c>table:content-validation</c>), <c>.DatabaseRanges</c> (filter/sort), and
/// <c>.StyleRegistry</c> (automatic-style interning). This root file holds the shared unit
/// conversions and ODF address formatters used by more than one of them.
/// </remarks>
public static partial class OdsContentXml
{
    private const double PxPerInch = 96.0;
    private const double CmPerInch = 2.54;

    private static string OdfRangeAddress(CellRange range, string fallbackSheet)
    {
        string sheet = range.Sheet ?? fallbackSheet;
        string start = OdfCellAddress(new CellAddress(range.Left, range.Top, sheet: sheet), fallbackSheet);
        if (range.RowCount == 1 && range.ColumnCount == 1)
        {
            return start;
        }

        string end = OdfCellAddress(new CellAddress(range.Right, range.Bottom, sheet: sheet), fallbackSheet);
        return $"{start}:{end}";
    }

    private static string OdfCellAddress(CellAddress address, string fallbackSheet)
    {
        string sheet = address.Sheet ?? fallbackSheet;
        return $"${sheet}.${address.ColumnLabel}${address.RowLabel}";
    }

    private static double PxToCm(double px) => px / PxPerInch * CmPerInch;

    private static double CmToPx(double cm) => cm / CmPerInch * PxPerInch;
}
