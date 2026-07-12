using System.Diagnostics;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// The hand-off to the host OS's file manager (PRD §7.2: "reveal in Finder"). It is the one command that answers
/// "where does this actually live?" — the question a notebook rooted in a plain folder invites — and every
/// desktop file tree offers it.
///
/// <para>Revealing means <em>select the entry inside its enclosing folder</em>, and that has no cross-platform
/// API: macOS spells it <c>open -R</c>, Windows <c>explorer /select,</c>, and Linux has no portable equivalent at
/// all. So the command is resolved here — returned as data, so it can be asserted on a platform that can't run
/// it — and a null means the caller should fall back to Avalonia's launcher, which can only open the enclosing
/// folder without selecting anything.</para>
/// </summary>
internal static class SystemFileManager
{
    /// <summary>What the host calls its file manager, for the context menu's header.</summary>
    public static string RevealLabel =>
        OperatingSystem.IsMacOS() ? "Reveal in Finder"
        : OperatingSystem.IsWindows() ? "Show in File Explorer"
        : "Show in File Manager";

    /// <summary>
    /// The command that reveals <paramref name="path"/> — selected — in the host file manager, or null on a
    /// platform that has none (Linux).
    /// </summary>
    public static ProcessStartInfo? RevealCommand(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            // ArgumentList rather than a joined string, because a note's path may well hold spaces.
            var open = new ProcessStartInfo("open") { UseShellExecute = false };
            open.ArgumentList.Add("-R");
            open.ArgumentList.Add(path);
            return open;
        }

        if (OperatingSystem.IsWindows())
        {
            // Explorer is the exception: it doesn't parse the fully-quoted "/select,<path>" token that
            // ArgumentList would produce, so this one is spelled as a raw argument string.
            return new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false };
        }

        return null;
    }

    /// <summary>
    /// Reveal <paramref name="path"/> in the host file manager, or return false when the platform has no reveal
    /// command and the caller should fall back to opening the enclosing folder.
    /// </summary>
    public static bool TryReveal(string path)
    {
        if (RevealCommand(path) is not { } command)
        {
            return false;
        }

        using var process = Process.Start(command);
        return true;
    }
}
