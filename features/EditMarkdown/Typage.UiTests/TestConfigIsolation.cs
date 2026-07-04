using System.Runtime.CompilerServices;

namespace Typage.UiTests;

/// <summary>
/// Redirects the per-user app-config directory to a throwaway location for the whole test run, so tests
/// that persist settings (e.g. <c>SetViewModeForTest</c> saving the view mode) never read or clobber the
/// developer's real <c>~/.config/Typage/settings.json</c>. Without this the file leaks state <i>between</i>
/// runs — a first run that persists <c>View=RawText</c> makes the next run's "defaults to Rich" assertions
/// fail. <see cref="Environment.SpecialFolder.ApplicationData"/> resolves via <c>$XDG_CONFIG_HOME</c> on
/// Linux and macOS (.NET follows the XDG base-dir spec on Unix), which the <see cref="SettingsService"/>
/// path is built from — so setting it here isolates every <c>SettingsService</c> the tests construct.
/// </summary>
internal static class TestConfigIsolation
{
    [ModuleInitializer]
    internal static void Init()
    {
        // A fresh directory each run (deleted then recreated) guarantees tests start from defaults rather
        // than a previous run's persisted state.
        var dir = Path.Combine(Path.GetTempPath(), "typage-uitests-config");
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
