using System.Diagnostics;

namespace Puffinote.Components.Core.Cli;

/// <summary>
/// Opens a document in one of the family's desktop apps from that app's CLI. Every one of the five
/// needs the identical, fiddly plumbing — find the installed app, hand it a path, fail with something a
/// person can act on — so it lives here rather than five times over.
///
/// <para>On macOS that means asking Launch Services for the installed bundle (<c>open -a Typage …</c>),
/// which is the only way to reach an instance that is already running instead of starting a second one.
/// Elsewhere it spawns the desktop binary published beside the CLI.</para>
/// </summary>
public static class AppLauncher
{
    /// <summary>
    /// Launch <paramref name="appName"/> (the bundle and executable name — "Typage", "Pivotask", …) on
    /// <paramref name="path"/>, or empty when it is null. Returns 0; throws <see cref="CliException"/>
    /// when the path names nothing or the app can't be found, so the caller's <see cref="CliHost"/> turns
    /// it into a clean one-line error.
    /// </summary>
    public static int Open(string appName, string? path)
    {
        if (path is not null && !File.Exists(path) && !Directory.Exists(path))
        {
            throw new CliException($"'{path}' does not exist.");
        }

        var start = StartInfo(appName, path) ?? throw new CliException(
            $"{appName} isn't installed. Run `just install-{appName.ToLowerInvariant()}-app` on macOS, or " +
            $"`just publish-{appName.ToLowerInvariant()}` so the desktop binary sits beside this one.");

        try
        {
            using var process = Process.Start(start);
            return 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new CliException($"Couldn't launch {appName}: {ex.Message}");
        }
    }

    private static ProcessStartInfo? StartInfo(string appName, string? path)
    {
        if (OperatingSystem.IsMacOS())
        {
            // `open -a` reaches the installed bundle, running or not. The document travels as an Apple
            // Event rather than in argv — which is why every desktop app in the family also handles a
            // file activation, and not only desktop.Args (see DocumentActivation).
            var info = new ProcessStartInfo("open") { UseShellExecute = false };
            info.ArgumentList.Add("-a");
            info.ArgumentList.Add(appName);
            if (path is not null)
            {
                info.ArgumentList.Add(path);
            }

            return info;
        }

        // Elsewhere: the desktop binary published alongside this CLI — either literally beside it, or in
        // the sibling `desktop/` folder that `just publish-<app>` writes next to `cli/`.
        string name = OperatingSystem.IsWindows() ? $"{appName}.exe" : appName;
        string? exe = new[]
        {
            Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(AppContext.BaseDirectory, "..", "desktop", name),
        }.FirstOrDefault(candidate => File.Exists(candidate) && !IsThisProcess(candidate));
        if (exe is null)
        {
            return null;
        }

        var spawn = new ProcessStartInfo(exe) { UseShellExecute = false };
        if (path is not null)
        {
            spawn.ArgumentList.Add(path);
        }

        return spawn;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is the binary already running. The desktop app and its CLI
    /// differ only in case — <c>Typage</c> beside <c>typage</c> — and <b>not every filesystem this runs on
    /// can tell them apart</b>: a macOS volume is case-insensitive by default, and so is the bind mount a
    /// Linux container gets from one. On those, "is the desktop binary here?" answers yes for a directory
    /// holding only the CLI, and the launcher hands the document straight back to itself — a process that
    /// spawns its successor and exits, forever, with the file never opening. Compared case-insensitively
    /// for the same reason the check is needed at all.
    /// </summary>
    private static bool IsThisProcess(string candidate) =>
        Environment.ProcessPath is { } self
        && string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase);
}
