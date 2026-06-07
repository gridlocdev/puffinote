namespace Cellestra.Core.Model;

/// <summary>
/// Per-row layout state: height, hidden flag (manual or filter-driven), and an optional
/// row-wide style. Only rows that differ from the defaults are stored on a worksheet.
/// </summary>
public sealed class Row
{
    /// <summary>Row height in device-independent pixels.</summary>
    public double Height { get; set; } = Defaults.RowHeight;

    /// <summary>Whether the row is hidden (PRD §8.14 / ODF <c>table:visibility</c>).</summary>
    public bool Hidden { get; set; }

    /// <summary>Optional default style index for cells in this row (0 = none).</summary>
    public int StyleId { get; set; }

    public bool IsDefault => Height == Defaults.RowHeight && !Hidden && StyleId == 0;

    public Row Clone() => new() { Height = Height, Hidden = Hidden, StyleId = StyleId };
}
