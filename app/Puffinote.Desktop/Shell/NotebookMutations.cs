namespace Puffinote.Desktop.Shell;

/// <summary>
/// The notebook's filesystem edit operations behind the tree's new/rename/delete actions (PRD §7.2). Pure
/// filesystem logic (no Avalonia), so the validation + move/create/delete rules are unit-testable without a
/// window. A rejected name throws <see cref="IOException"/> with a user-facing message; the shell surfaces
/// it through the same "couldn't do it" message path as a failed save. Puffinote never rewrites link
/// references on a move — link-refactor is a non-goal (PRD §7.2).
/// </summary>
internal static class NotebookMutations
{
    /// <summary>Create an empty file named <paramref name="name"/> in <paramref name="directory"/>; returns its path.</summary>
    public static string CreateFile(string directory, string name)
    {
        var path = Combine(directory, name);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"“{name}” already exists.");
        }

        File.WriteAllText(path, string.Empty);
        return path;
    }

    /// <summary>Create a folder named <paramref name="name"/> in <paramref name="directory"/>; returns its path.</summary>
    public static string CreateFolder(string directory, string name)
    {
        var path = Combine(directory, name);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"“{name}” already exists.");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Rename the file/folder at <paramref name="path"/> to <paramref name="newName"/>; returns the new path.</summary>
    public static string Rename(string path, string newName)
    {
        var parent = System.IO.Path.GetDirectoryName(path)
            ?? throw new IOException("Can't rename the notebook root.");
        var target = Combine(parent, newName);
        if (string.Equals(target, path, StringComparison.Ordinal))
        {
            return path; // unchanged
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException($"“{newName}” already exists.");
        }

        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }

        return target;
    }

    /// <summary>Delete the file, or the folder (recursively), at <paramref name="path"/>.</summary>
    public static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }
    }

    // Validate a single path segment and join it — rejecting empties, separators, traversal, and reserved
    // dot-names, so a mutation can only ever touch a direct child of the target directory.
    private static string Combine(string directory, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new IOException("Enter a name.");
        }

        if (trimmed is "." or ".."
            || trimmed.IndexOfAny(new[] { '/', '\\' }) >= 0
            || trimmed.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new IOException($"“{name}” isn't a valid name.");
        }

        return System.IO.Path.Combine(directory, trimmed);
    }
}
