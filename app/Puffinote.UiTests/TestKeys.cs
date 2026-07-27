using Avalonia.Input;
using Puffinote.Components.Commands;

namespace Puffinote.UiTests;

/// <summary>
/// The keyboard facts a test must not guess at, taken from the same places the shell takes them.
/// <para>
/// This suite was written on Linux CI and assumed it twice over: that the command modifier is Ctrl (it is
/// ⌘ on macOS, where the shell binds <see cref="Shortcut"/>'s platform gesture), and that a bare Delete
/// deletes in the file tree (on macOS that is deliberately inert — deletion is ⌘⌫, so a stray keypress
/// throws nothing away). Neither is a claim the tests meant to make about the host they run on.
/// </para>
/// </summary>
internal static class TestKeys
{
    /// <summary>The command modifier the shell binds on this host: ⌘ on macOS, Ctrl elsewhere.</summary>
    internal static KeyModifiers Command { get; } = Shortcut.Cmd(Key.None).ModifiersFor(Shortcut.IsMac);

    /// <summary>The same modifier as raw input, for <c>KeyPressQwerty</c>.</summary>
    internal static RawInputModifiers RawCommand { get; } =
        Shortcut.IsMac ? RawInputModifiers.Meta : RawInputModifiers.Control;

    /// <summary>
    /// The delete gesture the file tree honours on this host — ⌘⌫ on macOS, a bare Delete elsewhere
    /// (see <c>FileTreeView.UseMacKeymap</c>).
    /// </summary>
    internal static (PhysicalKey Key, RawInputModifiers Modifiers) DeleteInTree { get; } =
        Shortcut.IsMac
            ? (PhysicalKey.Backspace, RawInputModifiers.Meta)
            : (PhysicalKey.Delete, RawInputModifiers.None);
}
