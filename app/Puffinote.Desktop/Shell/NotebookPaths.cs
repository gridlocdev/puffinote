namespace Puffinote.Desktop.Shell;

/// <summary>
/// Display-oriented path helpers shared across the shell (distinct from <see cref="PathScope"/>, which
/// reconciles renames): render a notebook file relative to its root for labels. Uses a separator boundary so a
/// sibling like <c>…/notes-archive</c> is never mistaken for living under <c>…/notes</c>.
/// </summary>
internal static class NotebookPaths
{
    /// <summary>
    /// <paramref name="path"/> rendered relative to <paramref name="root"/> (e.g. <c>sub/note.md</c>), the
    /// empty string when it <em>is</em> the root, or the path unchanged when it isn't under the root.
    /// </summary>
    public static string RelativeTo(string? root, string path)
    {
        if (root is null)
        {
            return path;
        }

        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var prefix = root.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? root
            : root + System.IO.Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..] : path;
    }
}
