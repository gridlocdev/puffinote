namespace Cellestra.Core.Model;

/// <summary>
/// Per-column layout state: width, hidden flag, and an optional column-wide style.
/// Only columns that differ from the defaults are stored on a worksheet.
/// </summary>
public sealed class Column
{
    /// <summary>Column width in device-independent pixels.</summary>
    public double Width { get; set; } = Defaults.ColumnWidth;

    /// <summary>Whether the column is hidden (PRD §8.14 / ODF <c>table:visibility</c>).</summary>
    public bool Hidden { get; set; }

    /// <summary>Optional default style index for cells in this column (0 = none).</summary>
    public int StyleId { get; set; }

    public bool IsDefault => Width == Defaults.ColumnWidth && !Hidden && StyleId == 0;

    public Column Clone() => new() { Width = Width, Hidden = Hidden, StyleId = StyleId };
}
