using Avalonia.Input;
using Puffinote.Components.Commands;
using Typage.Desktop.Services;
using Typage.Desktop.Views;

namespace Typage.UiTests;

/// <summary>
/// Fixtures for tests that stand up the real shell window, and the platform facts they must not guess at.
/// <para>
/// Both exist because this suite was written on Linux CI and quietly assumed it: a window built the plain
/// way inherits the developer's own profile (a leftover crash-recovery snapshot then opens a modal over
/// every test), and a shortcut written as <c>Ctrl</c> is only the command modifier off macOS — where the
/// app binds ⌘. Neither assumption is anything the tests meant to make.
/// </para>
/// </summary>
internal static class TestShell
{
    /// <summary>
    /// The command modifier the app actually binds on this host — ⌘ on macOS, Ctrl elsewhere. Taken from
    /// the shared <see cref="Shortcut"/> the shell binds with, so a test can never disagree with the app.
    /// </summary>
    internal static KeyModifiers Command { get; } = Shortcut.Cmd(Key.None).ModifiersFor(Shortcut.IsMac);

    /// <summary>The same modifier as raw input, for <c>KeyPressQwerty</c>.</summary>
    internal static RawInputModifiers RawCommand { get; } =
        Shortcut.IsMac ? RawInputModifiers.Meta : RawInputModifiers.Control;

    /// <summary>
    /// A shell window whose crash-recovery store is a scratch path that does not exist, so no recovery
    /// prompt can open over the test. The directory is only created if something actually autosaves.
    /// </summary>
    internal static MainWindow NewWindow(SettingsService? settings = null) =>
        new(settings ?? new SettingsService(),
            new RecoveryService(Path.Combine(Path.GetTempPath(), $"typage-uitests-{Guid.NewGuid():N}")));
}
