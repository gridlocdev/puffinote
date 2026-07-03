using Avalonia.Controls;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// The file-tree panel (PRD §7.2): wraps the existing <see cref="FileTreeView"/> as a sidebar panel and
/// re-surfaces its file-activation as an absolute path, so the shell opens the file into a tab.
/// </summary>
internal sealed class FilesPanel : ISidebarPanel
{
    private readonly FileTreeView _tree = new();

    public FilesPanel()
    {
        _tree.FileActivated += node => FileActivated?.Invoke(node.Path);
    }

    public string Id => "files";

    public string Title => "Files";

    public Control View => _tree;

    /// <summary>Raised when a file is activated in the tree (double-click / Enter).</summary>
    public event Action<string>? FileActivated;

    public void Focus() => _tree.FocusTree();

    /// <summary>Rebuild the tree for a newly opened notebook (or clear it when <paramref name="root"/> is null).</summary>
    public void SetRoot(NotebookNode? root) => _tree.SetRoot(root);
}
