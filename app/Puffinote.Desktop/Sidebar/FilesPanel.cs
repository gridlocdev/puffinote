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
        _tree.SelectionChanged += node => SelectionChanged?.Invoke(node);
        _tree.RenameRequested += node => RenameRequested?.Invoke(node);
        _tree.DeleteRequested += node => DeleteRequested?.Invoke(node);
        _tree.NewFileRequested += node => NewFileRequested?.Invoke(node);
        _tree.NewFolderRequested += node => NewFolderRequested?.Invoke(node);
    }

    public string Id => "files";

    public string Title => "Files";

    public Control View => _tree;

    /// <summary>Raised when a file is activated in the tree (double-click / Enter).</summary>
    public event Action<string>? FileActivated;

    /// <summary>Raised when the tree selection changes (for command-enablement refresh).</summary>
    public event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Tree mutation requests (F2 / Delete / context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RenameRequested;
    public event Action<NotebookNode>? DeleteRequested;
    public event Action<NotebookNode?>? NewFileRequested;
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>The tree's selected node, or null.</summary>
    public NotebookNode? SelectedNode => _tree.SelectedNode;

    public void Focus() => _tree.FocusTree();

    /// <summary>Rebuild the tree for a newly opened notebook (or clear it when <paramref name="root"/> is null).</summary>
    public void SetRoot(NotebookNode? root) => _tree.SetRoot(root);

    /// <summary>Re-read the tree from disk after a mutation, preserving expansion + selection.</summary>
    public void Refresh() => _tree.Refresh();

    /// <summary>Reveal and select <paramref name="path"/> in the tree (expanding its ancestors).</summary>
    public void SelectPath(string path) => _tree.SelectPath(path);
}
