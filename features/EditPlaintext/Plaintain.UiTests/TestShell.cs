using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Puffinote.Components.Core.Documents;

namespace Plaintain.UiTests;

/// <summary>
/// How a headless test builds the window. The one thing it must not do is take the real profile's
/// crash-recovery directory: a snapshot left there by <em>any</em> unclean shutdown — the app's own, at
/// any point in the past — greets every window with the "unsaved changes were recovered" modal, and every
/// test that never mentions recovery then fails on a developer's machine and passes on fresh CI. Typage's
/// shell carries the same seam, for the same reason.
/// </summary>
internal static class TestShell
{
    private static readonly string ProfileRoot =
        Path.Combine(Path.GetTempPath(), $"plaintain-tests-{Guid.NewGuid():N}");

    /// <summary>A window over <paramref name="settings"/>, with recovery pointed at a scratch directory.</summary>
    public static MainWindow NewWindow(SettingsService settings) =>
        new(settings, new RecoveryService("Plaintain", Path.Combine(ProfileRoot, $"recovery-{Guid.NewGuid():N}")));
}
