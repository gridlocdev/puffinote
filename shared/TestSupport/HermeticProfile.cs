using System.Runtime.CompilerServices;

namespace Puffinote.TestSupport;

/// <summary>
/// Points every "where does this user live" lookup at a throwaway directory, before a single test runs.
/// </summary>
/// <remarks>
/// The apps in this family read and write real files in the real user profile — settings, recovery
/// snapshots, staged images — and a UI test that opens a window gets the same code path a launched app
/// does. That makes the developer's own profile shared mutable state between tests: one test that changed
/// a setting and let the host persist it would silently rewrite the profile, and every later test that
/// opened a window inherited the change. It reads as an unrelated failure somewhere far away, days later,
/// on one machine only — the failure mode this exists to make impossible.
///
/// <para>
/// A per-test scratch path fixes one test; this fixes the suite, including tests nobody has written yet.
/// It runs as a <see cref="ModuleInitializerAttribute"/>, so it is in place before any test — or any
/// static holding a path — has had a chance to look. Every app derives its locations from one of the
/// variables set below, either through <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>
/// (which resolves <c>XDG_CONFIG_HOME</c> / <c>APPDATA</c> and falls back to <c>HOME</c>) or by reading
/// <c>HOME</c> itself, as Cellestra's settings service does.
/// </para>
///
/// <para>
/// This is a floor, not a licence: a test that cares about a path should still name its own, the way the
/// Preferences tests hand their window a scratch settings file. What this guarantees is that forgetting
/// to costs a stray temp directory rather than the developer's profile.
/// </para>
/// </remarks>
internal static class HermeticProfile
{
    /// <summary>The throwaway profile this test process runs against.</summary>
    /// <remarks>
    /// Resolved before <c>HOME</c> is reassigned. The temp root itself comes from <c>TMPDIR</c>, which is
    /// not derived from the home directory, so the two never chase each other.
    /// </remarks>
    internal static string Root { get; } = Path.Combine(
        Path.GetTempPath(), $"puffinote-test-profile-{Environment.ProcessId}-{Guid.NewGuid():N}");

    [ModuleInitializer]
    internal static void Redirect()
    {
        Directory.CreateDirectory(Root);

        // HOME/USERPROFILE are the fallback every other location is derived from; the XDG and AppData
        // variables are what GetFolderPath prefers on Linux and Windows respectively. Setting all of them
        // keeps the redirect identical on the three platforms this repo builds for, rather than working on
        // the developer's macOS and quietly not on CI's Linux.
        //
        // HOME earns its place separately: Cellestra keeps its settings in the macOS
        // ~/Library/Application Support directory, which no XDG variable reaches, and reads HOME itself to
        // find it. XDG_DATA_HOME earns its place the same way — Typage stages images under
        // LocalApplicationData, which XDG_CONFIG_HOME does not cover, so a config-only redirect left real
        // files in ~/.local/share/Typage.
        //
        // Each target directory is created before it is named. .NET returns "" from GetFolderPath when the
        // variable points at a directory that does not exist, and an app joining paths onto "" writes into
        // the working directory instead — a redirect that silently becomes a different kind of mess.
        Redirect("HOME", Root);
        Redirect("USERPROFILE", Root);
        Redirect("XDG_CONFIG_HOME", Path.Combine(Root, ".config"));
        Redirect("XDG_DATA_HOME", Path.Combine(Root, ".local", "share"));
        Redirect("XDG_CACHE_HOME", Path.Combine(Root, ".cache"));
        Redirect("APPDATA", Path.Combine(Root, "AppData", "Roaming"));
        Redirect("LOCALAPPDATA", Path.Combine(Root, "AppData", "Local"));

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Discard();
    }

    private static void Redirect(string variable, string path)
    {
        Environment.SetEnvironmentVariable(variable, path);
        Directory.CreateDirectory(path);
    }

    private static void Discard()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp directory left behind is not worth failing an otherwise green run over; the OS
            // reclaims it. Losing the run's result to a cleanup race would be the worse trade.
        }
    }
}
