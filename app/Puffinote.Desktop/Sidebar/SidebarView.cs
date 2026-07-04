using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// The sidebar's panel host (PRD §7.2): a notebook-name header, a keyboard-focusable switcher row, and the
/// active panel's body. 1.0 ships the file tree and recent files; notebook search and the document outline
/// register alongside them in later sub-phases (each an <see cref="ISidebarPanel"/>) with no change here.
/// </summary>
internal sealed class SidebarView : UserControl
{
    private readonly FilesPanel _files = new();
    private readonly SearchPanel _search = new();
    private readonly OutlinePanel _outline = new();
    private readonly RecentPanel _recent = new();
    private readonly List<ISidebarPanel> _panels;

    private readonly TextBlock _header = new()
    {
        Text = "NO NOTEBOOK OPEN",
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(12, 8),
    };
    private readonly StackPanel _switcher = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 2,
        Margin = new Thickness(8, 0, 8, 6),
    };
    private readonly Border _body = new();
    private ISidebarPanel _active;

    public SidebarView()
    {
        _panels = [_files, _search, _outline, _recent];
        _active = _files;

        _files.FileActivated += path => FileActivated?.Invoke(path);
        _search.FileActivated += path => FileActivated?.Invoke(path);
        _recent.FileActivated += path => FileActivated?.Invoke(path);
        _files.SelectionChanged += node => SelectionChanged?.Invoke(node);
        _files.RenameRequested += node => RenameRequested?.Invoke(node);
        _files.DeleteRequested += node => DeleteRequested?.Invoke(node);
        _files.NewNoteRequested += node => NewNoteRequested?.Invoke(node);
        _files.NewSpreadsheetRequested += node => NewSpreadsheetRequested?.Invoke(node);
        _files.NewFolderRequested += node => NewFolderRequested?.Invoke(node);
        _files.PinnedChanged += () => PinnedChanged?.Invoke();

        var palette = Tokens.Dark;
        _header.Foreground = palette.MutedText;

        BuildSwitcher(palette);
        _body.Child = _active.View;

        Content = new DockPanel
        {
            Children =
            {
                Dock(_header, Avalonia.Controls.Dock.Top),
                Dock(_switcher, Avalonia.Controls.Dock.Top),
                _body,
            },
        };
    }

    /// <summary>Raised when a file is activated in any panel (tree or recent), to open it in a tab.</summary>
    public event Action<string>? FileActivated;

    /// <summary>Raised when the file-tree selection changes (for command-enablement refresh).</summary>
    public event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Raised when a panel becomes active (so the shell can refresh e.g. the outline on view).</summary>
    public event Action<string>? PanelActivated;

    /// <summary>Raised when the file tree's pinned folders change, so the shell persists them to the session.</summary>
    public event Action? PinnedChanged;

    /// <summary>Tree mutation requests (F2 / Delete / context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RenameRequested;
    public event Action<NotebookNode>? DeleteRequested;
    public event Action<NotebookNode?>? NewNoteRequested;
    public event Action<NotebookNode?>? NewSpreadsheetRequested;
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>The file tree's selected node, or null.</summary>
    public NotebookNode? SelectedTreeNode => _files.SelectedNode;

    /// <summary>Re-read the file tree from disk after a mutation.</summary>
    public void RefreshTree() => _files.Refresh();

    /// <summary>Reveal and select <paramref name="path"/> in the file tree (switching to the Files panel).</summary>
    public void RevealInTree(string path)
    {
        Activate("files");
        _files.SelectPath(path);
    }

    /// <summary>The id of the currently shown panel (test seam).</summary>
    internal string ActivePanelId => _active.Id;

    /// <summary>The outline panel's current items (test seam).</summary>
    internal IReadOnlyList<Panes.OutlineItem> OutlineItemsForTest => _outline.ItemsForTest;

    /// <summary>Rebuild for a newly opened notebook: header text + the file tree (or clear when null).</summary>
    public void SetRoot(NotebookNode? root)
    {
        _header.Text = root is { } r ? r.Name.ToUpperInvariant() : "NO NOTEBOOK OPEN";
        _files.SetRoot(root);
    }

    /// <summary>Update the recent-files panel with <paramref name="paths"/> (most-recent first).</summary>
    public void SetRecent(string? notebookRoot, IReadOnlyList<string> paths) => _recent.SetItems(notebookRoot, paths);

    /// <summary>Restore the file tree's pinned folders for the opened notebook.</summary>
    public void SetPinnedFolders(IReadOnlyList<string> paths) => _files.SetPinned(paths);

    /// <summary>The file tree's pinned folders, in chip order (for session persistence).</summary>
    public IReadOnlyList<string> PinnedFolders => _files.PinnedPaths;

    /// <summary>Update the outline panel with the active document's structure (or empty).</summary>
    public void SetOutline(IReadOnlyList<Panes.OutlineItem> items) => _outline.SetItems(items);

    /// <summary>Point the search panel at the shell's search function (queries the background index).</summary>
    public void ConfigureSearch(Func<string, IReadOnlyList<Search.SearchHit>> search) => _search.SearchFunc = search;

    /// <summary>Set the search panel's background-index status line, re-running any active query.</summary>
    public void SetSearchStatus(string status) => _search.SetIndexStatus(status);

    /// <summary>Activate the file tree and move keyboard focus into it (the shell's "focus file tree" command).</summary>
    public void FocusTree()
    {
        Activate("files");
        _files.Focus();
    }

    /// <summary>Show the panel with <paramref name="id"/> (no-op for an unknown id).</summary>
    public void Activate(string id)
    {
        if (_panels.FirstOrDefault(p => p.Id == id) is not { } panel || ReferenceEquals(panel, _active))
        {
            return;
        }

        _active = panel;
        _body.Child = panel.View;
        RefreshSwitcher();
        PanelActivated?.Invoke(panel.Id);
    }

    private void BuildSwitcher(ThemePalette palette)
    {
        foreach (var panel in _panels)
        {
            var button = new Button
            {
                Content = panel.Title,
                Tag = panel.Id,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4),
                FontSize = 12,
            };
            button.Click += (_, _) => Activate(panel.Id);
            _switcher.Children.Add(button);
        }

        RefreshSwitcher();
    }

    private void RefreshSwitcher()
    {
        var palette = Tokens.Dark;
        foreach (var button in _switcher.Children.OfType<Button>())
        {
            bool active = (string?)button.Tag == _active.Id;
            button.Foreground = active ? palette.Accent : palette.MutedText;
            button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }

    private static T Dock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }
}
