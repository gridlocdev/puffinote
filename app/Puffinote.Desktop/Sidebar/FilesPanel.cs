using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// The file-tree panel (PRD §7.2): a row of <b>pinned-folder chips</b> over the scoped <see cref="FileTreeView"/>.
/// Tapping a chip re-roots the tree at that folder; the "All notes" chip returns to the whole notebook. Folders
/// are pinned/unpinned from their tree context menu and unpinned from a chip's context menu; chips drag to
/// reorder. The panel owns the pin list + scope and re-surfaces file-activation as an absolute path so the
/// shell opens the file into a tab.
/// </summary>
internal sealed class FilesPanel : ISidebarPanel
{
    private const double DragThreshold = 4;

    private readonly FileTreeView _tree = new();
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _breadcrumb;
    private readonly DockPanel _view;

    private NotebookNode? _root;

    /// <summary>The pinned folders in chip order (absolute paths), for persistence and the chip row.</summary>
    public IReadOnlyList<string> PinnedPaths { get; private set; } = [];

    /// <summary>Absolute path of the scoped folder; null = "All notes" (the whole notebook).</summary>
    private string? Scope { get; set; }

    private Border? _allNotesChip;
    private string? _dragPath;
    private Point _dragStart;
    private bool _dragActive;
    private Border? _dropHighlight;

    public FilesPanel()
    {
        _tree.FileActivated += node => FileActivated?.Invoke(node.Path);
        _tree.SelectionChanged += node => SelectionChanged?.Invoke(node);
        _tree.RenameRequested += node => RenameRequested?.Invoke(node);
        _tree.DeleteRequested += node => DeleteRequested?.Invoke(node);
        _tree.NewNoteRequested += node => NewNoteRequested?.Invoke(node);
        _tree.NewSpreadsheetRequested += node => NewSpreadsheetRequested?.Invoke(node);
        _tree.NewFolderRequested += node => NewFolderRequested?.Invoke(node);
        _tree.PinRequested += node => Pin(node.Path);
        _tree.UnpinRequested += node => Unpin(node.Path);

        var palette = Tokens.Dark;

        // "Pinned folders" said what the chips *are* but not what they *do*, and since a pinned folder also
        // appears in the tree below, the same name showed up twice with no explained difference — is this a
        // filter, a shortcut, a duplicate? They are scope filters: clicking one re-roots the tree at that folder,
        // and "All notes" resets it. Naming them for their effect is the whole fix.
        var pinnedLabel = new TextBlock
        {
            Text = "Show only",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = palette.MutedText,
            Margin = new Thickness(2, 0, 0, 7),
        };
        ToolTip.SetTip(pinnedLabel, "Pinned folders. Click one to narrow the tree below to it.");
        _chips.Margin = new Thickness(0);
        var header = new StackPanel
        {
            Margin = new Thickness(11, 10, 11, 10),
            Children = { pinnedLabel, _chips },
        };
        DockPanel.SetDock(header, Dock.Top);

        _breadcrumb = new TextBlock
        {
            FontSize = 11,
            Foreground = palette.MutedText,
            Margin = new Thickness(14, 4, 14, 4),
            IsVisible = false,
        };
        DockPanel.SetDock(_breadcrumb, Dock.Top);

        _view = new DockPanel { Children = { header, _breadcrumb, _tree } };
        RenderChips();
    }

    public string Id => "files";

    public string Title => "Files";

    public Control View => _view;

    /// <summary>Raised when a file is activated in the tree (double-click / Enter).</summary>
    public event Action<string>? FileActivated;

    /// <summary>Raised when the tree selection changes (for command-enablement refresh).</summary>
    public event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Tree mutation requests (F2 / Delete / context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RenameRequested;
    public event Action<NotebookNode>? DeleteRequested;
    public event Action<NotebookNode?>? NewNoteRequested;
    public event Action<NotebookNode?>? NewSpreadsheetRequested;
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>Raised whenever the pinned-folder set or order changes, so the shell persists it to the session.</summary>
    public event Action? PinnedChanged;

    /// <summary>The tree's selected node, or null.</summary>
    public NotebookNode? SelectedNode => _tree.SelectedNode;

    public void Focus() => _tree.FocusTree();

    /// <summary>Rebuild the tree for a newly opened notebook (or clear it when <paramref name="root"/> is null).</summary>
    public void SetRoot(NotebookNode? root)
    {
        _root = root;
        PinnedPaths = [];
        Scope = null;
        _tree.SetRoot(root);
        RenderChips();
        RenderScope();
    }

    /// <summary>Restore the notebook's pinned folders (dropping any whose folder no longer exists).</summary>
    public void SetPinned(IReadOnlyList<string> pinned)
    {
        PinnedPaths = [.. pinned.Where(Directory.Exists)];
        Scope = null;
        RenderChips();
        RenderScope();
    }

    /// <summary>Re-read the tree from disk after a mutation, preserving expansion, and prune vanished pins/scope.</summary>
    public void Refresh()
    {
        var pruned = PinnedPaths.Where(Directory.Exists).ToList();
        bool pinsChanged = pruned.Count != PinnedPaths.Count;
        PinnedPaths = pruned;

        if (Scope is { } scope && !Directory.Exists(scope))
        {
            ScopeTo(null); // scoped folder was deleted/renamed → fall back to All notes (rebuilds the tree)
        }
        else
        {
            _tree.Refresh();
            RenderChips();
        }

        RenderScope();
        if (pinsChanged)
        {
            PinnedChanged?.Invoke();
        }
    }

    /// <summary>Reveal and select <paramref name="path"/> in the tree, returning to All notes if it's outside the scope.</summary>
    public void SelectPath(string path)
    {
        if (Scope is { } scope && !PathScope.IsUnderOrEqual(System.IO.Path.GetFullPath(path), scope))
        {
            ScopeTo(null);
        }

        _tree.SelectPath(path);
    }

    // ---- Pins & scope ---------------------------------------------------------------------------

    private void Pin(string path)
    {
        var next = PinnedFolders.Pin(PinnedPaths, path);
        if (ReferenceEquals(next, PinnedPaths))
        {
            return; // already pinned
        }

        PinnedPaths = next;
        RenderChips();
        PinnedChanged?.Invoke();
    }

    private void Unpin(string path)
    {
        if (!PinnedFolders.Contains(PinnedPaths, path))
        {
            return;
        }

        PinnedPaths = PinnedFolders.Unpin(PinnedPaths, path);
        if (string.Equals(Scope, path, StringComparison.OrdinalIgnoreCase))
        {
            ScopeTo(null); // scoped into a folder we just unpinned → back to All notes (also re-renders chips)
        }
        else
        {
            RenderChips();
        }

        PinnedChanged?.Invoke();
    }

    private void ScopeTo(string? path)
    {
        Scope = path;
        var displayRoot = path is null ? _root : new NotebookNode(path, isDirectory: true);
        if (displayRoot is not null)
        {
            _tree.SetScope(displayRoot);
        }

        RenderChips();
        RenderScope();
    }

    // ---- Chips ----------------------------------------------------------------------------------

    private void RenderChips()
    {
        _chips.Children.Clear();
        _allNotesChip = MakeChip("All notes", path: null);
        _chips.Children.Add(_allNotesChip);
        foreach (var path in PinnedPaths)
        {
            _chips.Children.Add(MakeChip(System.IO.Path.GetFileName(path), path));
        }

        _tree.SetPinnedPaths(new HashSet<string>(PinnedPaths, StringComparer.OrdinalIgnoreCase));
    }

    private Border MakeChip(string label, string? path)
    {
        var palette = Tokens.Dark;
        bool active = string.Equals(Scope, path, StringComparison.OrdinalIgnoreCase);

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = active ? palette.Accent : palette.MutedText,
        };

        var chip = new Border
        {
            Child = text,
            Tag = path,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(active ? 2 : 1), // the active filter is thicker, not only brighter
            Padding = new Thickness(11, 5),
            MinHeight = 26,
            Margin = new Thickness(0, 0, 6, 6),
            Background = active ? palette.AccentSubtle : Brushes.Transparent,
            BorderBrush = active ? palette.Accent : palette.SurfaceBorder,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(chip, path is null ? "Show the whole notebook" : $"Show only {label}");
        AutomationProperties.SetName(chip, label);
        AutomationProperties.SetHelpText(chip, active ? "selected" : null);

        if (path is null)
        {
            chip.Tapped += (_, _) => ScopeTo(null);
        }
        else
        {
            chip.ContextMenu = BuildChipMenu(path);
            chip.PointerPressed += (_, e) => OnChipPressed(chip, path, e);
            chip.PointerMoved += (_, e) => OnChipMoved(e);
            chip.PointerReleased += (_, e) => OnChipReleased(path, e);
        }

        return chip;
    }

    private ContextMenu BuildChipMenu(string path)
    {
        var unpin = new MenuItem { Header = "Unpin" };
        unpin.Click += (_, _) => Unpin(path);
        return new ContextMenu { ItemsSource = new List<Control> { unpin } };
    }

    private void RenderScope()
    {
        if (Scope is null || _root is null)
        {
            _breadcrumb.IsVisible = false;
            return;
        }

        var relative = System.IO.Path.GetRelativePath(_root.Path, Scope);
        var segments = relative.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        _breadcrumb.Text = string.Join("  /  ", new[] { _root.Name }.Concat(segments));
        _breadcrumb.IsVisible = true;
    }

    // ---- Chip drag-to-reorder -------------------------------------------------------------------

    private void OnChipPressed(Border chip, string path, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed)
        {
            return; // let the right-click context menu through
        }

        _dragPath = path;
        _dragStart = e.GetPosition(_chips);
        _dragActive = false;
        e.Pointer.Capture(chip);
    }

    private void OnChipMoved(PointerEventArgs e)
    {
        if (_dragPath is null)
        {
            return;
        }

        var pos = e.GetPosition(_chips);
        if (!_dragActive)
        {
            if (Math.Abs(pos.X - _dragStart.X) < DragThreshold && Math.Abs(pos.Y - _dragStart.Y) < DragThreshold)
            {
                return; // not moved far enough yet — still a potential click
            }

            _dragActive = true;
        }

        HighlightDropTarget(ChipAt(pos));
    }

    private void OnChipReleased(string path, PointerReleasedEventArgs e)
    {
        if (_dragPath is null)
        {
            return;
        }

        var pos = e.GetPosition(_chips);
        bool dragged = _dragActive;
        e.Pointer.Capture(null);
        _dragPath = null;
        _dragActive = false;
        HighlightDropTarget(null);

        if (!dragged)
        {
            ScopeTo(path); // a plain click on the chip scopes the tree to that folder
            return;
        }

        ApplyDrop(path, pos);
    }

    private void ApplyDrop(string src, Point pos)
    {
        var target = ChipAt(pos);
        IReadOnlyList<string> next;
        if (target is null || ReferenceEquals(target, _allNotesChip))
        {
            next = PinnedFolders.MoveToFront(PinnedPaths, src); // dropped on / before "All notes"
        }
        else if (target.Tag is string targetPath)
        {
            bool before = pos.X < target.Bounds.Center.X;
            next = PinnedFolders.Reorder(PinnedPaths, src, targetPath, before);
        }
        else
        {
            return;
        }

        if (!ReferenceEquals(next, PinnedPaths))
        {
            PinnedPaths = next;
            RenderChips();
            PinnedChanged?.Invoke();
        }
    }

    private Border? ChipAt(Point pos) =>
        _chips.Children.OfType<Border>().FirstOrDefault(c => c.Bounds.Contains(pos));

    private void HighlightDropTarget(Border? target)
    {
        if (ReferenceEquals(target, _dropHighlight))
        {
            return;
        }

        ResetChipBorder(_dropHighlight); // restore the previously-highlighted chip to its resting border
        _dropHighlight = target;
        if (target is not null && !ReferenceEquals(target, FindDraggedChip()))
        {
            target.BorderBrush = Tokens.Dark.Accent;
        }
    }

    private void ResetChipBorder(Border? chip)
    {
        if (chip is null)
        {
            return;
        }

        chip.BorderBrush = string.Equals(Scope, chip.Tag as string, StringComparison.OrdinalIgnoreCase)
            ? Tokens.Dark.Accent
            : Tokens.Dark.SurfaceBorder;
    }

    private Border? FindDraggedChip() =>
        _chips.Children.OfType<Border>().FirstOrDefault(c => string.Equals(c.Tag as string, _dragPath, StringComparison.OrdinalIgnoreCase));

    // ---- Test seams -----------------------------------------------------------------------------

    internal IReadOnlyList<string> ChipLabelsForTest =>
        [.. _chips.Children.OfType<Border>().Select(c => (c.Child as TextBlock)?.Text ?? string.Empty)];

    internal string? ScopePathForTest => Scope;

    internal bool BreadcrumbVisibleForTest => _breadcrumb.IsVisible;

    internal IReadOnlyList<string> TreeVisibleNamesForTest => _tree.VisibleNamesForTest;

    internal void PinForTest(string path) => Pin(path);

    internal void UnpinForTest(string path) => Unpin(path);

    internal void ScopeToForTest(string? path) => ScopeTo(path);

    internal void ReorderForTest(string src, string target, bool before)
    {
        var next = PinnedFolders.Reorder(PinnedPaths, src, target, before);
        if (ReferenceEquals(next, PinnedPaths))
        {
            return;
        }

        PinnedPaths = next;
        RenderChips();
        PinnedChanged?.Invoke();
    }
}
