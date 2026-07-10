using Puffinote.Components.Shell;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// The notebook-scoped recent-files list (PRD §7.2.3) together with its maintenance: promotion (through the
/// shared <see cref="RecentFiles"/> rule) plus the rename-remap and delete-drop reconciliation the tree needs
/// when files move or vanish (PRD §7.2). Holds absolute paths, most-recent first. The reconciling mutators
/// return whether the list actually changed, so the shell re-renders and re-persists only when it did.
/// </summary>
internal sealed class RecentFilesList
{
    private List<string> _paths = [];

    /// <summary>The current list, most-recent first (absolute paths).</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>Replace the list with <paramref name="paths"/>, dropping any whose file no longer exists.</summary>
    public void Load(IEnumerable<string> paths) => _paths = [.. paths.Where(File.Exists)];

    /// <summary>Promote <paramref name="path"/> to the most-recent slot (de-duplicated, capped).</summary>
    public void Push(string path) => _paths = RecentFiles.Push(_paths, path);

    /// <summary>
    /// Remap entries after a rename of <paramref name="oldPath"/> — the file itself, or a folder they live
    /// under — to <paramref name="newPath"/>. Returns whether any entry changed.
    /// </summary>
    public bool Remap(string oldPath, string newPath)
    {
        var old = System.IO.Path.GetFullPath(oldPath);
        var updated = System.IO.Path.GetFullPath(newPath);
        var changed = false;
        for (var i = 0; i < _paths.Count; i++)
        {
            if (PathScope.Remap(_paths[i], old, updated) is { } remapped)
            {
                _paths[i] = remapped;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Drop entries for a deleted file, or everything under a deleted folder <paramref name="path"/>. Returns
    /// whether any entry was dropped.
    /// </summary>
    public bool DropUnder(string path)
    {
        var target = System.IO.Path.GetFullPath(path);
        var kept = _paths.Where(p => !PathScope.IsUnderOrEqual(p, target)).ToList();
        if (kept.Count == _paths.Count)
        {
            return false;
        }

        _paths = kept;
        return true;
    }
}
