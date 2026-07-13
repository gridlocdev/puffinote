using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Puffinote.Components.Controls.Icons;
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
    private readonly TextBlock _reorderHint;
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
        _tree.RevealRequested += node => RevealRequested?.Invoke(node);
        _tree.OpenExternallyRequested += node => OpenExternallyRequested?.Invoke(node);

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

        // That chips can be dragged into an order is invisible until you try it — so say so, but only once
        // there are two of them and the ordering can actually mean something.
        _reorderHint = new TextBlock
        {
            Text = "drag to reorder",
            FontSize = 11,
            Foreground = palette.MutedText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 7),
            IsVisible = false,
        };

        pinnedLabel.VerticalAlignment = VerticalAlignment.Center;
        var pinnedHeader = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(pinnedLabel, Dock.Left);
        DockPanel.SetDock(_reorderHint, Dock.Right);
        pinnedHeader.Children.Add(pinnedLabel);
        pinnedHeader.Children.Add(_reorderHint);

        _chips.Margin = new Thickness(0);
        var header = new StackPanel
        {
            Margin = new Thickness(11, 10, 11, 10),
            Children = { pinnedHeader, _chips },
        };
        DockPanel.SetDock(header, Dock.Top);

        // A hairline under the pinned row: the chips *act on* the tree below, so they are a control strip, not
        // the first thing in the list. Without the rule they read as one continuous column of clickable things.
        var divider = new Border
        {
            Height = 1,
            Background = palette.SurfaceBorder,
            Opacity = 0.6,
        };
        DockPanel.SetDock(divider, Dock.Top);

        _breadcrumb = new TextBlock
        {
            FontSize = 11,
            Foreground = palette.MutedText,
            Margin = new Thickness(14, 8, 14, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = false,
        };
        DockPanel.SetDock(_breadcrumb, Dock.Top);

        _view = new DockPanel { Children = { header, divider, _breadcrumb, _tree } };
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

    /// <summary>Hand-offs to the host OS (tree context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RevealRequested;
    public event Action<NotebookNode>? OpenExternallyRequested;

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

    /// <summary>Mark <paramref name="path"/> (the active tab's document, or null) as the bold row in the tree.</summary>
    public void SetActiveDocument(string? path) => _tree.SetActiveDocument(path);

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

        _reorderHint.IsVisible = PinnedPaths.Count >= 2;
        _tree.SetPinnedPaths(new HashSet<string>(PinnedPaths, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// One scope chip. Each carries a glyph — the stacked-layers mark for the whole notebook, a folder for a pin
    /// — so a chip announces its kind before it is read, and the row of them reads as a set rather than as a
    /// paragraph of loose words. The unpin ✕ is revealed by hovering the chip: the same command lives in the
    /// chip's context menu, which stays the keyboard-reachable route.
    /// </summary>
    private Border MakeChip(string label, string? path)
    {
        var palette = Tokens.Dark;
        bool active = string.Equals(Scope, path, StringComparison.OrdinalIgnoreCase);
        bool isAllNotes = path is null;

        var glyph = new LucideIcon
        {
            IconKey = isAllNotes ? "layers" : FileGlyph.FolderIcon(expanded: false),
            Size = 13,
            Foreground = active ? palette.Accent : isAllNotes ? palette.MutedText : FileGlyph.Folder,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = active ? FontWeight.Medium : FontWeight.Normal,
            Foreground = active ? palette.Accent : palette.MutedText,
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { glyph, text },
        };

        var chip = new Border
        {
            Child = content,
            Tag = path,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(active ? 2 : 1), // the active filter is thicker, not only brighter
            Padding = new Thickness(10, 4, isAllNotes ? 11 : 6, 4),
            MinHeight = 26,
            Margin = new Thickness(0, 0, 6, 6),
            Background = active ? palette.AccentSubtle : Brushes.Transparent,
            BorderBrush = active ? palette.Accent : palette.SurfaceBorder,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(chip, isAllNotes ? "Show the whole notebook" : $"Show only {label}");
        AutomationProperties.SetName(chip, label);
        AutomationProperties.SetHelpText(chip, active ? "selected" : null);

        if (path is null)
        {
            chip.Tapped += (_, _) => ScopeTo(null);
            return chip;
        }

        var unpin = MakeUnpinButton(label, path, palette);
        content.Children.Add(unpin);
        chip.PointerEntered += (_, _) => unpin.Opacity = 1;
        chip.PointerExited += (_, _) => unpin.Opacity = 0;

        chip.ContextMenu = BuildChipMenu(path);
        chip.PointerPressed += (_, e) => OnChipPressed(chip, path, e);
        chip.PointerMoved += (_, e) => OnChipMoved(e);
        chip.PointerReleased += (_, e) => OnChipReleased(path, e);
        return chip;
    }

    /// <summary>
    /// The chip's unpin ✕. Deliberately smaller than WCAG 2.2 §2.5.8's 24px target floor — a 24px hit box inside
    /// a 26px chip would swallow the chip itself, and the chip's own job (click to scope) is the more common one.
    /// The criterion's <em>Equivalent</em> exception covers it: the same command sits in the chip's context menu,
    /// and in the tree row's, both full-size targets.
    /// </summary>
    private Button MakeUnpinButton(string label, string path, ThemePalette palette)
    {
        var unpin = new Button
        {
            Content = new LucideIcon { IconKey = "x", Size = 9, Foreground = palette.MutedText },
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsTabStop = false,
            Opacity = 0, // revealed on chip hover
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        unpin.Click += (_, _) => Unpin(path);
        ToolTip.SetTip(unpin, $"Unpin {label}");
        AutomationProperties.SetName(unpin, $"Unpin {label}");
        return unpin;
    }

    private ContextMenu BuildChipMenu(string path)
    {
        var unpin = new MenuItem { Header = "Unpin" };
        unpin.Click += (_, _) => Unpin(path);
        return new ContextMenu { ItemsSource = new List<Control> { unpin } };
    }

    /// <summary>The deepest crumbs a trail shows before it starts with an ellipsis instead.</summary>
    private const int MaxCrumbs = 3;

    /// <summary>
    /// Where the tree is currently rooted, as a trail of crumbs. The folder you are actually <em>in</em> is the
    /// last one, so it is the one drawn in full-strength text — the crumbs before it are the route, not the
    /// destination.
    ///
    /// <para>The trail is relative to the notebook and does not repeat its name: the sidebar header two rows above
    /// already says which notebook is open (with the full path on hover), and the notebook's folder name is
    /// typically the longest crumb of the lot. Spending the trail's width restating it pushed the one crumb that
    /// matters — the folder you scoped to — off the end of the line and into an ellipsis.</para>
    /// </summary>
    private void RenderScope()
    {
        if (Scope is null || _root is null)
        {
            _breadcrumb.IsVisible = false;
            return;
        }

        var palette = Tokens.Dark;
        var relative = System.IO.Path.GetRelativePath(_root.Path, Scope);
        var segments = relative.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        // A deep scope keeps its tail — the route is what gets elided, never the destination.
        bool elided = segments.Length > MaxCrumbs;
        var crumbs = elided ? segments[^MaxCrumbs..] : segments;

        var inlines = new InlineCollection();
        if (elided)
        {
            inlines.Add(new Run("…  /  ") { Foreground = palette.SurfaceBorder });
        }

        for (var i = 0; i < crumbs.Length; i++)
        {
            if (i > 0)
            {
                inlines.Add(new Run("  /  ") { Foreground = palette.SurfaceBorder });
            }

            // The destination crumb keeps the inherited default text brush; only the route is muted. (Assigning
            // `null` to mean "the default" would set a local null brush and paint nothing at all.)
            bool last = i == crumbs.Length - 1;
            var crumb = new Run(crumbs[i]) { FontWeight = last ? FontWeight.Medium : FontWeight.Normal };
            if (!last)
            {
                crumb.Foreground = palette.MutedText;
            }

            inlines.Add(crumb);
        }

        _breadcrumb.Inlines = inlines;
        ToolTip.SetTip(_breadcrumb, Scope); // the full path, for when the trail is elided
        _breadcrumb.IsVisible = true;
    }

    /// <summary>Shut every folder in the tree (the sidebar's "Collapse all" button).</summary>
    public void CollapseAll() => _tree.CollapseAll();

    // ---- Chip drag-to-reorder -------------------------------------------------------------------

    private void OnChipPressed(Border chip, string path, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed)
        {
            return; // let the right-click context menu through
        }

        // The unpin ✕ sits inside the chip. Its Click unpins; the press must not also start a drag or, on
        // release, scope the tree to a folder that is about to stop being pinned at all.
        if ((e.Source as Visual)?.FindAncestorOfType<Button>() is not null)
        {
            return;
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

    /// <summary>A chip's label is now one child among a glyph and an unpin button, so read it out of the row.</summary>
    internal IReadOnlyList<string> ChipLabelsForTest =>
    [
        .. _chips.Children.OfType<Border>()
            .Select(c => c.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty),
    ];

    internal string? ScopePathForTest => Scope;

    internal bool BreadcrumbVisibleForTest => _breadcrumb.IsVisible;

    internal string BreadcrumbTextForTest =>
        _breadcrumb.Inlines is null ? string.Empty : string.Concat(_breadcrumb.Inlines.OfType<Run>().Select(r => r.Text));

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
