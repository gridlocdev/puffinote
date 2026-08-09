using Puffinote.Components.Commands;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// The window-independent rules behind Puffinote's one shell command catalog (PRD §7.4 / §7.6): which command
/// ids are reserved global keys, which of those the shell already supersedes for the editor, and how a
/// command's effective gesture resolves against the user's keymap. The catalog <em>table</em> itself (titles,
/// actions, enablement) is built in the window, where the actions naturally close over it; these rules are
/// pulled out so they're unit-testable and can't drift between the reserved-key router, the conflict scanner,
/// and the menu.
/// </summary>
internal static class ShellCommandCatalog
{
    /// <summary>
    /// The moderate reserved global set (PRD §7.6): these shell keys win over the focused editor via the
    /// tunnelling handler, so e.g. ⌘K always opens Puffinote's palette rather than a sheet's. Their gestures
    /// are fixed — never routed through the user keymap.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedIds = new HashSet<string>
    {
        "shell.commandMenu", "shell.quickOpen", "shell.openNotebook", "shell.toggleSidebar",
        "shell.closeTab", "shell.nextTab", "shell.prevTab",
    };

    /// <summary>
    /// Reserved keys whose capability the shell already provides, so shadowing an editor's binding on them
    /// loses nothing: the merged palette (⌘K) is a superset of the editor's own, and quick-open (⌘O) is the
    /// shell's file-open. Excluded from the conflict warning so it only fires on a genuinely lost binding.
    /// </summary>
    public static readonly IReadOnlySet<string> ShellSupersedesEditor = new HashSet<string>
    {
        "shell.commandMenu", "shell.quickOpen",
    };

    /// <summary>Whether <paramref name="id"/> is a reserved global key (its gesture is fixed, PRD §7.6).</summary>
    public static bool IsReserved(string id) => ReservedIds.Contains(id);

    /// <summary>
    /// A command's effective gesture: its fixed default <paramref name="def"/> when reserved (never rebindable,
    /// PRD §7.6), else the user's custom binding from <paramref name="keymap"/> if any, otherwise the default.
    /// </summary>
    public static Shortcut? ResolveGesture(ShortcutKeymap keymap, string id, Shortcut? def) =>
        IsReserved(id) ? def : keymap.Resolve(id, def);
}
