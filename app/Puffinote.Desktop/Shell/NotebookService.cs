namespace Puffinote.Desktop.Shell;

/// <summary>
/// Owns the single open notebook (PRD §7.1 — one root folder at a time). Phase 0 keeps this minimal:
/// open a folder and expose its root <see cref="NotebookNode"/>. Later phases add per-notebook
/// <c>.puffinote/</c> state (session, settings, search index), a <see cref="System.IO.FileSystemWatcher"/>
/// for live tree refresh, and prompt-to-save on switch.
/// </summary>
public sealed class NotebookService
{
    /// <summary>The open notebook's root folder, or null when none is open.</summary>
    public NotebookNode? Root { get; private set; }

    /// <summary>The open notebook's absolute path, or null when none is open.</summary>
    public string? RootPath => Root?.Path;

    /// <summary>Raised after a notebook is opened (or switched), so the shell can rebuild the tree.</summary>
    public event EventHandler? NotebookOpened;

    /// <summary>
    /// Open <paramref name="path"/> as the notebook. Any existing directory is a valid notebook (no import
    /// step — PRD §8); the <c>.puffinote/</c> state directory is created lazily on first write, later.
    /// </summary>
    /// <returns>True when the folder exists and was opened.</returns>
    public bool OpenNotebook(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        Root = new NotebookNode(System.IO.Path.GetFullPath(path), isDirectory: true);
        NotebookOpened?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Every file in the open notebook, absolute-pathed, in tree order (folders' contents before siblings,
    /// directories before files at each level — the <see cref="NotebookNode"/> sort). Dotfiles and the
    /// <c>.puffinote/</c> state directory are skipped (they never enter the tree). Empty when no notebook is
    /// open. Feeds quick-open (PRD §7.2.5) and, later, the background search index (§7.2.3).
    /// </summary>
    public IReadOnlyList<string> EnumerateFiles()
    {
        var files = new List<string>();
        if (Root is { } root)
        {
            Collect(root, files);
        }

        return files;
    }

    private static void Collect(NotebookNode directory, List<string> files)
    {
        foreach (var child in directory.EnumerateChildren())
        {
            if (child.IsDirectory)
            {
                Collect(child, files);
            }
            else
            {
                files.Add(child.Path);
            }
        }
    }
}
