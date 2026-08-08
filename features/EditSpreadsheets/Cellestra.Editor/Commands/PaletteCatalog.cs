using Puffinote.Components.Controls;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// The spreadsheet's command palette contents, in one place: every currently-runnable command as a palette
/// row, grouped under a section header. Both palette surfaces build from this — ⌘K's centred overlay and the
/// search pill's dropped flyout — so the two always offer the same commands.
/// </summary>
internal static class PaletteCatalog
{
    /// <summary>The placeholder both surfaces show in their search box.</summary>
    internal const string Placeholder = "Type a command — bold, currency, sort…";

    /// <summary>
    /// The runnable commands as palette rows. Call this per open, never cache it: what's enabled depends on the
    /// current selection (there is nothing to unmerge or paste until there is).
    /// </summary>
    internal static IReadOnlyList<PaletteEntry> Entries(AppCommands commands) =>
    [
        .. commands.All
            .Where(c => c.IsEnabled)
            .Select(c => new PaletteEntry(c.Title, () => c.Execute(), c.Description, c.MacShortcut(), c.Icon, Category(c.Id))),
    ];

    /// <summary>Friendly category name for a command, derived from its id prefix, used as the palette's section header.</summary>
    private static string Category(string id)
    {
        string prefix = id.Contains('.', StringComparison.Ordinal) ? id[..id.IndexOf('.', StringComparison.Ordinal)] : id;
        return prefix switch
        {
            "file" => "File",
            "edit" => "Edit",
            "format" => "Format",
            "insert" => "Insert",
            "sheet" => "Insert",
            "data" => "Data",
            "formula" => "Formulas",
            "view" => "View",
            "help" => "Help",

            // Preferences is the one "app." command, and a section of its own is the honest place for it:
            // it configures the app rather than the workbook, and it used to fall through to "Other" —
            // which reads as a leftovers bin, exactly where you don't look for settings.
            "app" => "App",
            _ => "Other",
        };
    }
}
