using Avalonia.Input;

namespace Puffinote.Components.Commands;

/// <summary>
/// A single entry in an app's one command catalog (the "one catalog" golden rule): its id, a display
/// title, a plain-language description, an optional icon key and platform-agnostic <see cref="Shortcut"/>,
/// the action to run, and optional enabled / toggle-active checks. The menu bar, command bar, Command
/// Palette, and key bindings all bind to these, so those surfaces can never drift apart.
/// </summary>
/// <remarks>
/// The command is <b>environment-agnostic</b> (proposal 0001 §5): <see cref="Execute"/> and the checks are
/// parameterless. An app that needs an ambient environment (Cellestra's <c>IAppEnvironment</c>) captures
/// it in the closures when it builds the catalog, so the same type serves every app in the family and the
/// palette/menu stay free of app references.
/// </remarks>
public sealed class AppCommand
{
    public AppCommand(
        string id,
        string title,
        string description,
        Shortcut? shortcut,
        Action execute,
        Func<bool>? canExecute = null,
        Func<bool>? isActive = null,
        string? icon = null,
        IReadOnlyList<Shortcut>? alternateShortcuts = null)
    {
        Id = id;
        Title = title;
        Description = description;
        Shortcut = shortcut;
        Execute = execute;
        CanExecute = canExecute;
        IsActive = isActive;
        Icon = icon;
        AlternateShortcuts = alternateShortcuts ?? [];
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>A one-line, plain-language explanation shown in tooltips and the Command Palette.</summary>
    public string Description { get; }

    /// <summary>An icon key (e.g. a Lucide glyph name), or null when the command has no icon.</summary>
    public string? Icon { get; }

    /// <summary>The default, platform-agnostic shortcut, or null when the command has none.</summary>
    public Shortcut? Shortcut { get; }

    /// <summary>
    /// Extra chords that fire the same command but are <b>not</b> advertised — a menu, a tooltip, and the
    /// palette all show <see cref="Shortcut"/> alone, because a command with three keys listed reads as
    /// three commands. This is for the aliases a keyboard forces on us rather than ones a user should learn:
    /// Zoom In is ⌘= on the number row, but the key with "+" printed on it is ⇧= and the numpad has its own,
    /// and someone pressing what the menu spells as ⌘+ must not find it dead.
    /// </summary>
    public IReadOnlyList<Shortcut> AlternateShortcuts { get; }

    public Action Execute { get; }

    public Func<bool>? CanExecute { get; }

    /// <summary>For toggle-style commands: whether the toggle is currently "on".</summary>
    public Func<bool>? IsActive { get; }

    /// <summary>Whether the command can run right now (defaults to true when it has no <see cref="CanExecute"/>).</summary>
    public bool IsEnabled => CanExecute?.Invoke() ?? true;

    /// <summary>Whether this command renders as "on" (lit) right now (false when it isn't a toggle).</summary>
    public bool IsToggleActive => IsActive?.Invoke() ?? false;

    /// <summary>The shortcut rendered for display (e.g. "⌘S" on macOS, "Ctrl+S" elsewhere); empty when none.</summary>
    public string ShortcutText => Shortcut is { } s ? ShortcutFormatter.Format(s) : string.Empty;

    /// <summary>The bindable gesture for the current platform, or null when the command has no shortcut.</summary>
    public KeyGesture? Gesture => Shortcut?.ToGesture();

    /// <summary>
    /// Every shortcut that should fire this command — <see cref="Shortcut"/> first, then
    /// <see cref="AlternateShortcuts"/>. Hosts registering key bindings iterate this; anything that
    /// <em>displays</em> a shortcut uses <see cref="Shortcut"/>/<see cref="ShortcutText"/> instead.
    /// </summary>
    public IEnumerable<Shortcut> AllShortcuts =>
        Shortcut is { } s ? [s, .. AlternateShortcuts] : AlternateShortcuts;
}
