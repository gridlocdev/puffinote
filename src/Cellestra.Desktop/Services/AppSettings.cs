using System.Collections.Generic;

namespace Cellestra.Desktop.Services;

/// <summary>
/// User settings persisted between sessions (ARCHITECTURE §8): ribbon/zoom/view state, window
/// size, and the recent-files list. Stored as JSON under the app-support directory.
/// </summary>
public sealed record AppSettings
{
    public bool RibbonVisible { get; init; }

    public double Zoom { get; init; } = 1.0;

    public double WindowWidth { get; init; } = 1100;

    public double WindowHeight { get; init; } = 720;

    public List<string> RecentFiles { get; init; } = new();

    public const int MaxRecentFiles = 10;

    /// <summary>Return a copy with <paramref name="path"/> promoted to the most-recent slot (deduped, capped).</summary>
    public AppSettings WithRecentFile(string path)
    {
        var recent = new List<string> { path };
        recent.AddRange(RecentFiles.Where(p => !string.Equals(p, path, System.StringComparison.OrdinalIgnoreCase)));
        if (recent.Count > MaxRecentFiles)
        {
            recent.RemoveRange(MaxRecentFiles, recent.Count - MaxRecentFiles);
        }

        return this with { RecentFiles = recent };
    }
}
