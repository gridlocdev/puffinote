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
    public static string CreateFile(string directory, string name) => CreateFile(directory, name, []);

    /// <summary>
    /// Create a file named <paramref name="name"/> in <paramref name="directory"/> with the given initial
    /// <paramref name="content"/>; returns its path. Notes are created empty; a spreadsheet is created with a
    /// blank-workbook image, since an empty (0-byte) <c>.ods</c> isn't a valid workbook and won't open.
    /// </summary>
    public static string CreateFile(string directory, string name, byte[] content)
    {
        var path = Combine(directory, name);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"“{name}” already exists.");
        }

        File.WriteAllBytes(path, content);
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

        // A case-only change (e.g. "Readme.md" → "README.md") is the same entry on a case-insensitive
        // filesystem (macOS/Windows), so File.Exists(target) is true — don't treat that as a collision, or
        // the rename could never happen. The Move below still performs the recapitalization (C1).
        var caseOnlyRename = string.Equals(target, path, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyRename && (File.Exists(target) || Directory.Exists(target)))
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

    /// <summary>
    /// Whether the entry at <paramref name="path"/> can be moved into <paramref name="directory"/> — the
    /// question a drag over the tree asks on every pointer move, so it must be answerable without touching the
    /// disk twice or throwing. Three moves are refused: into the folder it is already in (nothing would
    /// happen), into itself, and into one of its own descendants (which would take the folder with it).
    /// </summary>
    public static bool CanMove(string path, string directory)
    {
        var source = System.IO.Path.GetFullPath(path);
        var target = System.IO.Path.GetFullPath(directory);
        return !PathScope.IsUnderOrEqual(target, source)
            && !string.Equals(System.IO.Path.GetDirectoryName(source), target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Move the file or folder at <paramref name="path"/> into <paramref name="directory"/>, keeping its name;
    /// returns the new path. A name already taken there is a refusal, not an overwrite — the drop that asked
    /// for it was aimed at a folder, not at the file it would have destroyed.
    /// </summary>
    public static string Move(string path, string directory)
    {
        if (!CanMove(path, directory))
        {
            throw new IOException("That folder can't hold it.");
        }

        var name = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        var target = Combine(directory, name);
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException($"“{name}” already exists in “{System.IO.Path.GetFileName(directory)}”.");
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
            || trimmed.IndexOfAny(['/', '\\']) >= 0
            || trimmed.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new IOException($"“{name}” isn't a valid name.");
        }

        return System.IO.Path.Combine(directory, trimmed);
    }
}
