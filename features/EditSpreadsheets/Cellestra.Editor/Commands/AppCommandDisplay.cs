using Puffinote.Components.Commands;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// Cellestra is a macOS-only app, so a command's shortcut always reads with the ⌘/⌥/⌃/⇧ glyphs
/// regardless of the host OS. The shared <see cref="AppCommand.ShortcutText"/> renders for the *current*
/// platform (right for the cross-platform siblings), which would read "Ctrl+…" on a non-mac test/CI host
/// — so every Cellestra surface displays a shortcut through this instead.
/// </summary>
public static class AppCommandDisplay
{
    /// <summary>The command's shortcut rendered with macOS glyphs (empty when it has none).</summary>
    public static string MacShortcut(this AppCommand command) =>
        command.Shortcut is { } s ? ShortcutFormatter.Format(s, mac: true) : string.Empty;
}
