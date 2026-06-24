using Avalonia.Input;

namespace Typage.Desktop.Commands;

/// <summary>
/// A single entry in the one command catalog (mirroring Cellestra's golden rule #4): its title,
/// a plain-language description, an optional platform-agnostic <see cref="Shortcut"/>, and the action to
/// run. The menu, the "Find a tool" box, and the Command Palette all read these, so the surfaces can't
/// drift apart — and each renders the shortcut natively for its platform.
/// </summary>
public sealed record AppCommand(
    string Id,
    string Title,
    string Description,
    Shortcut? Shortcut,
    Action Execute,
    Func<bool>? CanExecute = null)
{
    public bool IsEnabled => CanExecute?.Invoke() ?? true;

    /// <summary>The shortcut rendered for display (e.g. "⌘S" on macOS, "Ctrl+S" elsewhere); empty when none.</summary>
    public string ShortcutText => Shortcut is { } s ? ShortcutFormatter.Format(s) : string.Empty;

    /// <summary>The bindable gesture for the current platform, or null when the command has no shortcut.</summary>
    public KeyGesture? Gesture => Shortcut?.ToGesture();
}
