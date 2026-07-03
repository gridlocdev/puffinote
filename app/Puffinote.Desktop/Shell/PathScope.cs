namespace Puffinote.Desktop.Shell;

/// <summary>
/// Path arithmetic for reconciling open tabs and recent files when a file or folder is renamed or deleted
/// in the tree (PRD §7.2). Pure and case-insensitive; operates on already-absolute paths. When a folder is
/// the subject, every path <em>under</em> it is affected, not just an exact match.
/// </summary>
internal static class PathScope
{
    /// <summary>
    /// The new location of <paramref name="path"/> when <paramref name="oldPath"/> — the path itself or a
    /// folder containing it — is renamed to <paramref name="newPath"/>; null when <paramref name="path"/>
    /// is unaffected.
    /// </summary>
    public static string? Remap(string path, string oldPath, string newPath)
    {
        if (Equal(path, oldPath))
        {
            return newPath;
        }

        var prefix = WithSeparator(oldPath);
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? WithSeparator(newPath) + path[prefix.Length..]
            : null;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="ancestor"/> itself or lives under it.</summary>
    public static bool IsUnderOrEqual(string path, string ancestor) =>
        Equal(path, ancestor) || path.StartsWith(WithSeparator(ancestor), StringComparison.OrdinalIgnoreCase);

    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string WithSeparator(string path) =>
        path.EndsWith(System.IO.Path.DirectorySeparatorChar) ? path : path + System.IO.Path.DirectorySeparatorChar;
}
