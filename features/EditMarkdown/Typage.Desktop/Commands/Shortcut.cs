using Avalonia.Input;

namespace Typage.Desktop.Commands;

/// <summary>
/// A platform-agnostic keyboard shortcut: a key plus optional Shift/Alt, and whether it carries the
/// platform's <b>command</b> modifier (⌘ on macOS, Ctrl elsewhere). Defining shortcuts this way — once,
/// abstractly — lets the menus, key bindings, and Command Palette all render and bind them natively per
/// platform (PRD §7.5: a platform-native menu bar), instead of hard-coding "Ctrl+…" or "⌘…".
/// </summary>
public readonly record struct Shortcut(Key Key, KeyModifiers Extra = KeyModifiers.None, bool UsesCommand = true)
{
    /// <summary>True on macOS, where the command modifier is ⌘ (Meta) rather than Ctrl.</summary>
    public static bool IsMac => OperatingSystem.IsMacOS();

    /// <summary>A command-modifier shortcut: ⌘<paramref name="key"/> on macOS, Ctrl+<paramref name="key"/> elsewhere.</summary>
    public static Shortcut Cmd(Key key, KeyModifiers extra = KeyModifiers.None) => new(key, extra, UsesCommand: true);

    /// <summary>A bare-key shortcut with no command modifier (e.g. F11).</summary>
    public static Shortcut Bare(Key key, KeyModifiers extra = KeyModifiers.None) => new(key, extra, UsesCommand: false);

    /// <summary>The concrete modifiers for the given platform (the command modifier resolves to Meta or Control).</summary>
    public KeyModifiers ModifiersFor(bool mac) =>
        Extra | (UsesCommand ? (mac ? KeyModifiers.Meta : KeyModifiers.Control) : KeyModifiers.None);

    /// <summary>The bindable <see cref="KeyGesture"/> for the given platform.</summary>
    public KeyGesture ToGesture(bool mac) => new(Key, ModifiersFor(mac));

    /// <summary>The bindable <see cref="KeyGesture"/> for the current platform.</summary>
    public KeyGesture ToGesture() => ToGesture(IsMac);

    /// <summary>
    /// Whether this shortcut is a standard text-editing chord (⌘/Ctrl + A/C/V/X/Z/Y) that a focused text
    /// field must handle itself. Such a shortcut is given <b>no</b> macOS menu key-equivalent, so the OS
    /// doesn't swallow it mid-edit in a dialog field (it's still driven by the editor / the key binding).
    /// </summary>
    public bool YieldsToTextInput =>
        UsesCommand
        && (Extra == KeyModifiers.None || Extra == KeyModifiers.Shift)
        && Key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y;
}
