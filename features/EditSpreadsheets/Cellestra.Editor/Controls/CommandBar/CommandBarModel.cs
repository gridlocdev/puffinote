using System.Collections.Generic;

namespace Cellestra.Desktop.Controls.CommandBar;

/// <summary>
/// What the user has selected, which decides the bar's tool set. v1 only edits a sheet, so only
/// <see cref="Cell"/> is live; charts will add their own context (a future sheet-tab object) without
/// the bar growing tabs. Kept separate from <c>AppCommands</c> so the catalog stays the single source
/// of the commands themselves — this only describes how they're grouped and surfaced.
/// </summary>
public enum CommandContext
{
    Cell,
}

/// <summary>
/// One group in the adaptive command bar: a name, the 2–3 <see cref="Primary"/> command ids shown
/// as icons, and the <see cref="Overflow"/> ids revealed in the group's drawer. An empty
/// <see cref="Overflow"/> (and no special drawer content) means the group shows no chevron.
/// </summary>
public sealed record CommandBarGroup(string Name, IReadOnlyList<string> Primary, IReadOnlyList<string> Overflow)
{
    /// <summary>True for the Text group — bespoke drawer affordances for font size and colours.</summary>
    public bool HasStyleDrawer => Name == "Text";

    /// <summary>True for the Align group — adds vertical-alignment buttons alongside the horizontal ones.</summary>
    public bool HasAlignDrawer => Name == "Align";

    /// <summary>True for the Numbers group — adds the grouped, previewed "Format as" picker.</summary>
    public bool HasNumberDrawer => Name == "Numbers";

    /// <summary>Whether this group needs a chevron — it has overflow commands or any bespoke drawer content.</summary>
    public bool HasDrawer => Overflow.Count > 0 || HasStyleDrawer || HasAlignDrawer || HasNumberDrawer;
}

/// <summary>The bar layout per context. Group/primary/overflow ids all resolve in <c>AppCommands</c>.</summary>
public static class CommandBarModel
{
    public static IReadOnlyList<CommandBarGroup> For(CommandContext context) => context switch
    {
        _ => Cell,
    };

    private static readonly IReadOnlyList<CommandBarGroup> Cell = new[]
    {
        new CommandBarGroup("Edit",
            new[] { "edit.undo", "edit.redo" },
            // Format Painter sits with the clipboard tools — it's a "copy the look, paste it" action.
            // Paste Special and Fill Down/Right round out the clipboard/fill actions.
            new[] { "edit.cut", "edit.copy", "edit.paste", "edit.pasteSpecial", "edit.formatPainter", "fill.down", "fill.right", "edit.delete", "edit.clearFormats", "edit.clearAll" }),
        new CommandBarGroup("Text",
            new[] { "format.bold", "format.italic", "format.underline" },
            new[] { "format.strikethrough", "format.cells" }),
        new CommandBarGroup("Align",
            new[] { "format.alignLeft", "format.alignCenter", "format.alignRight" },
            new[] { "format.wrap", "format.merge" }),
        // Borders: a small group whose chevron drawer lists the common presets (recognition over
        // recall). The primary tool is "All Borders"; the rest live behind the drawer.
        new CommandBarGroup("Border",
            new[] { "format.borderAll" },
            new[] { "format.borderOutline", "format.borderNone", "format.borderBottom", "format.borderTop", "format.borderLeft", "format.borderRight" }),
        new CommandBarGroup("Numbers",
            new[] { "format.currency", "format.percent" },
            // The increase/decrease-decimal steppers live in the drawer (greyed on text, like the
            // other number tools); the previewed "Format as" picker (HasNumberDrawer) covers the
            // discrete formats, so no "Date" toggle is duplicated here.
            new[] { "format.increaseDecimal", "format.decreaseDecimal" }),
        // AutoSum: a flagship one-click "sum the column/row" action, surfaced on its own.
        new CommandBarGroup("Formula",
            new[] { "edit.autoSum" },
            System.Array.Empty<string>()),
        new CommandBarGroup("Insert",
            new[] { "insert.rowsAbove", "insert.columnsLeft" },
            new[] { "insert.sheet", "data.createTable" }),
        new CommandBarGroup("Data",
            new[] { "data.sortAsc", "data.sortDesc", "data.sortAndFilter" },
            new[] { "data.customSort", "data.toggleFilter", "data.clearFilter", "data.removeDuplicates", "data.textToColumns", "data.validation" }),
    };
}
