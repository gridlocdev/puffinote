using System;
using Avalonia.Input;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// A single command in the one catalog (Golden Rule #4). Carries its label, icon key, and
/// default shortcut plus the action and an optional enabled check. The menu bar, ribbon,
/// Command Palette, and keybindings all bind to these, so they can never drift apart.
/// </summary>
public sealed class AppCommand
{
    public AppCommand(string id, string label, string description, Action<IAppEnvironment> execute,
        string? icon = null, KeyGesture? shortcut = null, Func<IAppEnvironment, bool>? canExecute = null)
    {
        Id = id;
        Label = label;
        Description = description;
        Execute = execute;
        Icon = icon;
        Shortcut = shortcut;
        CanExecute = canExecute;
    }

    public string Id { get; }

    public string Label { get; }

    /// <summary>A one-line, plain-language explanation shown in tooltips and the Command Palette.</summary>
    public string Description { get; }

    public string? Icon { get; }

    public KeyGesture? Shortcut { get; }

    public Action<IAppEnvironment> Execute { get; }

    public Func<IAppEnvironment, bool>? CanExecute { get; }

    public bool IsEnabled(IAppEnvironment environment) => CanExecute?.Invoke(environment) ?? true;

    /// <summary>The shortcut rendered for display, e.g. "⌘S".</summary>
    public string ShortcutText => Shortcut is null ? string.Empty : ShortcutFormatter.Format(Shortcut);
}
