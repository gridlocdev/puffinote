using System.Runtime.CompilerServices;

namespace Puffinote.UiTests;

/// <summary>
/// Redirects the per-user app-config directory to a throwaway location for the whole test run, so tests
/// that persist shell state (Preferences writing <c>settings.json</c>, the last-opened notebook writing
/// <c>state.json</c>) never read or clobber the developer's real <c>~/.config/Puffinote/</c>. Without it a
/// run leaks state into the next one — and into the developer's own app. Mirrors the same guard in
/// Typage's suite. <see cref="Environment.SpecialFolder.ApplicationData"/> resolves via
/// <c>$XDG_CONFIG_HOME</c> on Linux and macOS (.NET follows the XDG base-dir spec on Unix), which is what
/// <c>SettingsStore.AppConfigPath</c> builds on — so setting it here isolates every store the tests touch.
/// </summary>
internal static class TestConfigIsolation
{
    [ModuleInitializer]
    internal static void Init()
    {
        // A fresh directory each run (deleted then recreated) guarantees tests start from defaults rather
        // than a previous run's persisted state.
        var dir = Path.Combine(Path.GetTempPath(), "puffinote-uitests-config");
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a stale lock shouldn't block the run — the dir is recreated below.
        }

        Directory.CreateDirectory(dir);
        // Must point at an existing directory: .NET returns "" for ApplicationData when XDG_CONFIG_HOME
        // names a directory that doesn't exist, so create it first (above).
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", dir);
    }
}
