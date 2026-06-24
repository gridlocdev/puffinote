namespace Typage.Desktop.Commands;

/// <summary>
/// A single entry in the one command catalog (mirroring Cellestra's golden rule #4): its title,
/// a plain-language description, an optional display shortcut, and the action to run. The menu, the
/// "Find a tool" box, and the Command Palette all read these, so the surfaces can't drift apart.
/// </summary>
public sealed record AppCommand(
    string Id,
    string Title,
    string Description,
    string? Shortcut,
    Action Execute,
    Func<bool>? CanExecute = null)
{
    public bool IsEnabled => CanExecute?.Invoke() ?? true;
}
