using Avalonia.Input;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// The reserved-vs-focus keyboard dispatch rule (PRD §7.6): a key in the reserved global set is handled by
/// the shell regardless of focus, so an editor can never shadow it; anything else falls through to whoever
/// has focus. This is the pure decision — given the reserved bindings and a pressed key, return the payload
/// (command) to run, or null to let the editor have the key — so it's unit-testable without a window.
/// </summary>
internal static class ShortcutRouter
{
    /// <summary>
    /// The payload bound to the first reserved gesture matching <paramref name="key"/> +
    /// <paramref name="modifiers"/>, or null when none matches (the editor keeps the key). Modifier match is
    /// exact — the same comparison the conflict scanner uses, so routing and conflict-detection agree.
    /// </summary>
    public static T? Match<T>(IEnumerable<(KeyGesture Gesture, T Command)> reserved, Key key, KeyModifiers modifiers)
        where T : class
    {
        foreach (var (gesture, command) in reserved)
        {
            if (gesture.Key == key && gesture.KeyModifiers == modifiers)
            {
                return command;
            }
        }

        return null;
    }
}
