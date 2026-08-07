using System.Diagnostics;
using Puffinote.Components.Core.Cli;

namespace Plaintain.Cli;

/// <summary>
/// Opens a file in Plaintain.Desktop. On macOS that means asking Launch Services for the installed
/// bundle (<c>open -a Plaintain</c>), which is the only way to reach a running instance rather than
/// starting a second one; elsewhere it spawns the published desktop binary beside this one.
/// </summary>
internal static class Launcher
{
    public static int Open(string? path)
    {
        if (path is not null && path != "-" && !File.Exists(path))
        {
            throw new CliException($"'{path}' does not exist.");
        }

        var start = StartInfo(path) ?? throw new CliException(
            "Plaintain isn't installed. Run `just install-plaintain-app` on macOS, or `just publish-plaintain` " +
            "so the desktop binary sits beside this one.");

        try
        {
            using var process = Process.Start(start);
            return 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new CliException($"Couldn't launch Plaintain: {ex.Message}");
        }
    }

    private static ProcessStartInfo? StartInfo(string? path)
    {
        if (OperatingSystem.IsMacOS())
        {
            var info = new ProcessStartInfo("open") { UseShellExecute = false };
            info.ArgumentList.Add("-a");
            info.ArgumentList.Add("Plaintain");
            if (path is not null)
            {
                info.ArgumentList.Add(path);
            }

            return info;
        }

        // Elsewhere: the desktop binary published alongside this CLI.
        string directory = AppContext.BaseDirectory;
        string exe = Path.Combine(directory, OperatingSystem.IsWindows() ? "Plaintain.exe" : "Plaintain");
        if (!File.Exists(exe))
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
}
