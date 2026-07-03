using Puffinote.Components.Commands;
using Puffinote.Components.Controls;

namespace Puffinote.Desktop.Commands;

/// <summary>
/// Composes the Command Palette entries: Puffinote's shell catalog merged with the active editor's catalog
/// (PRD §7.4). Each app still defines its catalog once in its own code; this only <em>composes</em> them at
/// display time — it never forks a catalog. Pure and headless-testable (takes command lists, returns rows).
/// </summary>
internal static class CommandMerge
{
    public const string ShellCategory = "Puffinote";
    public const string EditorCategory = "Editor";

    /// <summary>Enabled shell commands first (grouped under "Puffinote"), then enabled editor commands ("Editor").</summary>
    public static IReadOnlyList<PaletteEntry> ToEntries(
        IEnumerable<AppCommand> shell, IEnumerable<AppCommand> editor)
    {
        var entries = new List<PaletteEntry>();
        foreach (var command in shell.Where(c => c.IsEnabled))
        {
            entries.Add(Entry(command, ShellCategory));
        }

        foreach (var command in editor.Where(c => c.IsEnabled))
        {
            entries.Add(Entry(command, EditorCategory));
        }

        return entries;
    }

    private static PaletteEntry Entry(AppCommand command, string category) =>
        new(command.Title, () => command.Execute(), command.Description, command.ShortcutText, command.Icon, category);
}
