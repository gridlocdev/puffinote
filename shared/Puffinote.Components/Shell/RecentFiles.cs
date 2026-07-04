namespace Puffinote.Components.Shell;

/// <summary>
/// The shared recent-files rule both apps' settings duplicated: promote a path to the most-recent slot,
/// de-duplicated case-insensitively, capped at a maximum. Each app stores its own <c>RecentFiles</c>
/// list (in its settings type) and routes its "push a recent file" call through here.
/// </summary>
public static class RecentFiles
{
    /// <summary>The default cap on how many recent files to remember.</summary>
    public const int DefaultMax = 10;

    /// <summary>
    /// Return a new list with <paramref name="path"/> first, followed by <paramref name="current"/> minus
    /// any case-insensitive duplicate of it, truncated to <paramref name="max"/> entries.
    /// </summary>
    public static List<string> Push(IEnumerable<string> current, string path, int max = DefaultMax)
    {
        var recent = new List<string> { path };
        recent.AddRange(current.Where(p => !string.Equals(p, path, System.StringComparison.OrdinalIgnoreCase)));
        if (recent.Count > max)
        {
            recent.RemoveRange(max, recent.Count - max);
        }

        return recent;
    }
}
