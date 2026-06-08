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
    /// <summary>True when the Text group — the only one with bespoke drawer affordances (font size, colours).</summary>
    public bool HasStyleDrawer => Name == "Text";
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
            new[] { "edit.cut", "edit.copy", "edit.paste", "edit.delete", "edit.clearFormats", "edit.clearAll" }),
        new CommandBarGroup("Text",
            new[] { "format.bold", "format.italic", "format.underline" },
            new[] { "format.strikethrough", "format.cells" }),
        new CommandBarGroup("Align",
            new[] { "format.alignLeft", "format.alignCenter", "format.alignRight" },
            new[] { "format.wrap", "format.merge" }),
        new CommandBarGroup("Numbers",
            new[] { "format.currency", "format.percent" },
            new[] { "format.date" }),
        new CommandBarGroup("Insert",
            new[] { "insert.rowsAbove", "insert.columnsLeft" },
            new[] { "insert.sheet", "data.createTable" }),
        new CommandBarGroup("Data",
            new[] { "data.sortAsc", "data.toggleFilter" },
            new[] { "data.sortDesc", "data.customSort", "data.clearFilter", "data.removeDuplicates", "data.textToColumns", "data.validation" }),
    };
}
