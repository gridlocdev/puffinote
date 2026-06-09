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
    /// Default fill for a new table's header row, ARGB — the app's green accent (<c>#15734F</c>).
    /// Paired with white, bold text (<see cref="TableHeaderTextArgb"/>) for legible contrast.
    /// </summary>
    public const uint TableHeaderFillArgb = 0xFF15734F;

    /// <summary>Default text color for a new table's header row, ARGB. Opaque white (on the green fill).</summary>
    public const uint TableHeaderTextArgb = 0xFFFFFFFF;

    /// <summary>
    /// Generate the next unused default sheet name given the names already in use
    /// (Sheet2, Sheet3, … skipping any already taken).
    /// </summary>
    public static string NextSheetName(IReadOnlyCollection<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        for (int n = 1; ; n++)
        {
            string candidate = $"{SheetNamePrefix}{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
