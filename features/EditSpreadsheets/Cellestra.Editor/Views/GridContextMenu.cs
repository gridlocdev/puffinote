using Cellestra.Desktop.Controls.SpreadsheetGrid;

namespace Cellestra.Editor;

/// <summary>
/// What each grid context menu offers, as ids into the one command catalog (null = separator). Keeping
/// the contents here rather than inline in the view means the menus can be read — and asserted — without
/// driving a pointer, and it is the single place to answer "what does right-clicking <em>this</em> give me".
/// </summary>
/// <remarks>
/// A header menu offers what acts on a whole line: the clipboard, insert/delete, size and visibility,
/// then clearing, then Format Cells… — the same order the Format menu uses, so the two teach each other.
/// What it deliberately leaves out is anything that reads the selection <em>as data</em> (Sort, Filter,
/// Auto-fit aside): a header selects every row of its column, and a sort over a million rows is not what
/// the click meant.
/// </remarks>
internal static class GridContextMenu
{
    /// <summary>The command ids for the menu on <paramref name="target"/>, or an empty list when there is nothing to offer.</summary>
    internal static IReadOnlyList<string?> Ids(SpreadsheetGrid.ContextTarget target) => target switch
    {
        SpreadsheetGrid.ContextTarget.ColumnHeader =>
        [
            "edit.cut", "edit.copy", "edit.paste", null,
            "insert.columnsLeft", "edit.deleteColumns", null,
            "format.columnWidth", "data.autofitColumns", "format.hideColumns", "format.unhideColumns", null,
            "edit.delete", "edit.clearFormats", null,
            "format.cells",
        ],
        SpreadsheetGrid.ContextTarget.RowHeader =>
        [
            "edit.cut", "edit.copy", "edit.paste", null,
            "insert.rowsAbove", "edit.deleteRows", null,
            "format.rowHeight", "format.hideRows", "format.unhideRows", null,
            "edit.delete", "edit.clearFormats", null,
            "format.cells",
        ],
        SpreadsheetGrid.ContextTarget.Cell =>
        [
            "edit.cut", "edit.copy", "edit.paste", null,
            "insert.rowsAbove", "insert.columnsLeft", null,
            "edit.delete", "edit.clearFormats", "edit.clearAll", null,
            "format.merge", "format.cells",
        ],
        _ => [], // the top-left corner selects nothing, so it has nothing to offer
    };
}
