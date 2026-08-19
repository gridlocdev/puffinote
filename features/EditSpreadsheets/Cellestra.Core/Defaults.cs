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

    // Zoom bounds (PRD §4 — 50%–400%) live with the rest of the family's zoom ladder, in
    // Puffinote.Components' ZoomScale: they are a presentation concern shared with every sibling
    // editor, and this core is deliberately UI-free.

    /// <summary>The default text color, ARGB. Opaque black.</summary>
    public const uint TextColorArgb = 0xFF000000;

    /// <summary>
    /// Fill for the header row <b>Clean Up (§8.17)</b> writes into a cleaned block, ARGB — the same
    /// neutral grey the grid's light-theme table-header band uses (<c>#C6C6C6</c>), so a cleaned-up
    /// block and a table look like they came from the same program. Clean Up writes real, static
    /// formatting on purpose (that is its job); a <em>table's</em> header look, by contrast, is theme
    /// chrome painted by the grid and is never baked into cells. Paired with black, bold text
    /// (<see cref="TableHeaderTextArgb"/>); contrast ≈ 12:1, well past WCAG AAA (7:1).
    /// </summary>
    public const uint TableHeaderFillArgb = 0xFFC6C6C6;

    /// <summary>Text color for Clean Up's header row, ARGB. Opaque black (on the grey fill).</summary>
    public const uint TableHeaderTextArgb = 0xFF000000;

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
