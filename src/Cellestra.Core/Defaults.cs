namespace Cellestra.Core;

/// <summary>
/// The single home for new-workbook and cell defaults (PRD §8.8). Defined once here so
/// the model, styling, and UI never disagree. Sizes are in device-independent pixels.
/// </summary>
public static class Defaults
{
    /// <summary>The one worksheet a new workbook starts with.</summary>
    public const string FirstSheetName = "Sheet1";

    /// <summary>Prefix for auto-generated sheet names (Sheet2, Sheet3, …).</summary>
    public const string SheetNamePrefix = "Sheet";

    /// <summary>Prefix for auto-generated chart-sheet names (Chart1, Chart2, …).</summary>
    public const string ChartSheetNamePrefix = "Chart";

    /// <summary>Default cell font family (PRD §8.8).</summary>
    public const string FontFamily = "Helvetica Neue";

    /// <summary>Default font size in points.</summary>
    public const double FontSizePoints = 10d;

    /// <summary>Default column width (≈ 8 characters at the default font).</summary>
    public const double ColumnWidth = 64d;

    /// <summary>Default row height.</summary>
    public const double RowHeight = 20d;

    /// <summary>Minimum zoom factor (PRD §4 — 50%).</summary>
    public const double MinZoom = 0.5d;

    /// <summary>Maximum zoom factor (PRD §4 — 400%).</summary>
    public const double MaxZoom = 4.0d;

    /// <summary>The default text color, ARGB. Opaque black.</summary>
    public const uint TextColorArgb = 0xFF000000;

    /// <summary>
    /// Default fill for a new table's header row, ARGB — a saturated royal blue (<c>#1E4FB5</c>).
    /// Paired with white, bold text (<see cref="TableHeaderTextArgb"/>); contrast ≈ 7.39:1 with
    /// white, well past WCAG AA (4.5:1) and AAA (7:1) for legible header labels.
    /// </summary>
    public const uint TableHeaderFillArgb = 0xFF1E4FB5;

    /// <summary>Default text color for a new table's header row, ARGB. Opaque white (on the royal-blue fill).</summary>
    public const uint TableHeaderTextArgb = 0xFFFFFFFF;

    /// <summary>
    /// Generate the next unused default sheet name given the names already in use
    /// (Sheet2, Sheet3, … skipping any already taken).
    /// </summary>
    public static string NextSheetName(IReadOnlyCollection<string> existingNames) =>
        NextName(SheetNamePrefix, existingNames);

    /// <summary>
    /// Generate the next unused chart-sheet name given the names already in use
    /// (Chart1, Chart2, … skipping any already taken).
    /// </summary>
    public static string NextChartSheetName(IReadOnlyCollection<string> existingNames) =>
        NextName(ChartSheetNamePrefix, existingNames);

    private static string NextName(string prefix, IReadOnlyCollection<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        for (int n = 1; ; n++)
        {
            string candidate = $"{prefix}{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
