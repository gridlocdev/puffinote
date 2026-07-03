using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Controls;

/// <summary>
/// The notebook's read-only file tree (PRD §7.2). Phase 0 uses the first-party <see cref="TreeView"/> —
/// which already gives arrow-key navigation and expand/collapse for free — populated lazily from
/// <see cref="NotebookNode"/>. Phase 4 replaces this with a virtualized, fully keyboard-operable custom
/// tree (rename/new/delete, type-to-search) but keeps the same <see cref="NotebookNode"/> model.
/// </summary>
internal sealed class FileTreeView : UserControl
{
    private readonly TreeView _tree = new();

    public FileTreeView()
    {
        _tree.SelectionChanged += (_, _) =>
            SelectionChanged?.Invoke(_tree.SelectedItem is TreeViewItem { Tag: NotebookNode node } ? node : null);
        // Open a file on double-click or Enter (a folder just toggles, the TreeView's default).
        _tree.DoubleTapped += (_, _) => ActivateSelectedFile();
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ActivateSelectedFile();
            }
        };
        Content = _tree;
    }

    /// <summary>Raised when the selected entry changes (null when the selection is cleared).</summary>
    public event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Raised when a <em>file</em> entry is activated (double-click / Enter), to open it in a pane.</summary>
    public event Action<NotebookNode>? FileActivated;

    private void ActivateSelectedFile()
    {
        if (_tree.SelectedItem is TreeViewItem { Tag: NotebookNode { IsDirectory: false } node })
        {
            FileActivated?.Invoke(node);
        }
    }

    /// <summary>Move keyboard focus into the tree (the shell's "focus file tree" command).</summary>
    public void FocusTree() => _tree.Focus();

    /// <summary>Rebuild the tree for a newly opened notebook (or clear it when <paramref name="root"/> is null).</summary>
    public void SetRoot(NotebookNode? root)
    {
        if (root is null)
        {
            _tree.ItemsSource = null;
            return;
        }

        var rootItem = BuildItem(root);
        rootItem.IsExpanded = true; // show the notebook's top level immediately
        _tree.ItemsSource = new List<TreeViewItem> { rootItem };
    }

    // A directory item is seeded with a placeholder child so its expander chevron appears, then
    // populated once on first expand — so opening a large notebook never walks the whole tree up front.
    private static TreeViewItem BuildItem(NotebookNode node)
    {
        var item = new TreeViewItem { Header = node.Name, Tag = node };
        if (!node.IsDirectory)
        {
            return item;
        }

        item.ItemsSource = new List<TreeViewItem> { new() { Header = "…", IsEnabled = false } };
        var populated = false;
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property != TreeViewItem.IsExpandedProperty || populated || !item.IsExpanded)
            {
                return;
            }

            populated = true;
            item.ItemsSource = node.EnumerateChildren().Select(BuildItem).ToList();
        };
        return item;
    }
}
