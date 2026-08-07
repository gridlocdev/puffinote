using Cellestra.Core.Model;

namespace Cellestra.Core.Data;

/// <summary>
/// One rule: while you are scrolled down inside a table, the grid's column-header strip shows the
/// <i>table's</i> column titles instead of A/B/C. A table is a named shape with named columns, and the
/// moment its header row leaves the top of the window the columns lose their names — which is exactly
/// when a wide table stops being readable. The letters are only useful when there is nothing better.
/// </summary>
public static class StickyTableHeader
{
    /// <summary>
    /// The row whose cell should label <paramref name="column"/> in the header strip, or null to keep the
    /// column's letter.
    ///
    /// <para>Non-null only while all three hold: <paramref name="topRow"/> — the first scrollable row on
    /// screen — is inside a table at this column, that table has a header row, and that header row has
    /// scrolled above the fold. Each of the three failing is a case where the letter is the right answer:
    /// past the table's last row there is no table; with the real header on screen there is no need to
    /// repeat it; and a headerless table has no title to show.</para>
    /// </summary>
    public static int? LabelRow(Worksheet sheet, int column, int topRow)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        return sheet.FindTable(topRow, column) is { HasHeader: true } table && table.Range.Top < topRow
            ? table.Range.Top
            : null;
    }
}
