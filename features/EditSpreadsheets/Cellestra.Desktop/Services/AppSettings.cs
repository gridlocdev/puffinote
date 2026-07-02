using System.Collections.Generic;
using Cellestra.Desktop.Commands;
using RecentFilesHelper = Puffinote.Components.Shell.RecentFiles;

namespace Cellestra.Desktop.Services;

/// <summary>
/// User settings persisted between sessions (ARCHITECTURE §8): command-bar/zoom/view state, window
/// size, the recent-files list, and the radial-menu bindings. Stored as JSON under the app-support
/// directory.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Whether the adaptive command bar is folded to its slim handle. Default: expanded.</summary>
    public bool CommandBarCollapsed { get; init; }

    /// <summary>The press-and-hold radial command menu's segment count, labels, and per-segment bindings.</summary>
    public RadialMenuConfig RadialMenu { get; init; } = RadialMenuConfig.Default;

    public double Zoom { get; init; } = 1.0;

    public double WindowWidth { get; init; } = 1100;

    public double WindowHeight { get; init; } = 720;

    public List<string> RecentFiles { get; init; } = new();

    public const int MaxRecentFiles = 10;

    /// <summary>Return a copy with <paramref name="path"/> promoted to the most-recent slot (deduped, capped).</summary>
    public AppSettings WithRecentFile(string path) =>
        this with { RecentFiles = RecentFilesHelper.Push(RecentFiles, path, MaxRecentFiles) };
}
