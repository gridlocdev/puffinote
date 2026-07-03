namespace Puffinote.Desktop.Shell;

/// <summary>
/// One entry (file or folder) in the open notebook's tree. Pure filesystem logic — no Avalonia — so it is
/// unit-testable without a window. The read-only file tree in the sidebar is built from these; Phase 4
/// replaces the tree control with a virtualized, fully keyboard-operable one, but keeps this model.
/// </summary>
public sealed class NotebookNode
{
    /// <summary>The per-notebook state directory, hidden from the tree (PRD §7.1).</summary>
    public const string StateDirectoryName = ".puffinote";

    public NotebookNode(string path, bool isDirectory)
    {
        Path = path;
        IsDirectory = isDirectory;
        // A drive root (e.g. "/") has no file name; fall back to the full path so it still shows.
        var name = System.IO.Path.GetFileName(path);
        Name = string.IsNullOrEmpty(name) ? path : name;
    }

    public string Path { get; }

    public string Name { get; }

    public bool IsDirectory { get; }

    /// <summary>
    /// Child entries: directories first, then files, each alphabetical (case-insensitive). Hidden
    /// dot-entries and the <see cref="StateDirectoryName"/> are skipped. Returns empty for a file, an
    /// unreadable directory, or one that has been removed — the tree never throws on a bad entry.
    /// </summary>
    public IReadOnlyList<NotebookNode> EnumerateChildren()
    {
        if (!IsDirectory)
        {
            return Array.Empty<NotebookNode>();
        }

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<NotebookNode>();
        }

        var nodes = new List<NotebookNode>(entries.Length);
        foreach (var entry in entries)
        {
            var name = System.IO.Path.GetFileName(entry);
            if (string.IsNullOrEmpty(name) || name[0] == '.')
            {
                continue; // skip dotfiles and the .puffinote state directory
            }

            nodes.Add(new NotebookNode(entry, Directory.Exists(entry)));
        }

        nodes.Sort(static (a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1; // directories before files
            }

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return nodes;
    }
}
