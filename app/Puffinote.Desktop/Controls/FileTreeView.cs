using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Controls;

/// <summary>
/// The notebook's file tree (PRD §7.2), fully keyboard-operable (§7.2.2 — a hard requirement). It renders the
/// currently-visible nodes as a flat, virtualized <see cref="ListBox"/> of indented rows (folders'
/// expansion tracked by path), which gives real virtualization and total control over the key map:
/// <list type="bullet">
///   <item>Up/Down move the selection, Home/End jump to first/last (the ListBox).</item>
///   <item>Left collapses an open folder or ascends to the parent; Right expands a folder or descends into it.</item>
///   <item>F2 renames the selection; ⌘N makes a new note, ⌘⇧N a new folder (in / beside the selection).</item>
///   <item>Type-to-search jumps to the next node whose name matches the typed prefix.</item>
/// </list>
/// The bare-key <em>activation</em> and <em>delete</em> keys follow the host platform's editors (see
/// <see cref="UseMacKeymap"/>) so muscle memory carries over:
/// <list type="bullet">
///   <item><b>macOS</b> (Finder / Xcode / VS Code): <c>Return</c> renames, <c>Space</c> or <c>⌘↓</c> opens a
///   file / toggles a folder, <c>⌘↑</c> jumps to the enclosing folder, and <c>⌘⌫</c> deletes — a bare
///   <c>Delete</c> is deliberately inert so nothing is thrown away by a stray keypress.</item>
///   <item><b>Elsewhere</b>: <c>Enter</c> opens a file / toggles a folder and <c>Delete</c>/<c>Backspace</c>
///   deletes, as on Windows/Linux explorers.</item>
/// </list>
/// The <see cref="NotebookNode"/> model is unchanged; mutation actions (new/rename/delete) drive this through
/// <see cref="Refresh"/> / <see cref="SelectPath"/>.
/// </summary>
internal sealed class FileTreeView : UserControl
{
    private sealed record Row(NotebookNode Node, int Depth);

    private static readonly TimeSpan TypeaheadWindow = TimeSpan.FromSeconds(1);

    private readonly ListBox _list;
    private NotebookNode? _root;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<string> _pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private MenuItem _pinItem = null!;
    private MenuItem _unpinItem = null!;
    private string _typeahead = string.Empty;
    private DateTime _lastKeystroke = DateTime.MinValue;

    /// <summary>
    /// Whether the bare-key map follows macOS editor conventions (Return renames, Space/⌘↓ opens, ⌘↑ ascends,
    /// ⌘⌫ deletes) rather than the Windows/Linux one (Enter opens, Delete deletes). Defaults to the running OS;
    /// a test seam so either map can be exercised on the headless Linux CI.
    /// </summary>
    internal bool UseMacKeymap { get; set; } = OperatingSystem.IsMacOS();

    public FileTreeView()
    {
        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = false, // keyboard focus lives on the UserControl (below); the list is pointer-driven only
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => BuildRow(row), supportsRecycling: true),
        };
        StyleSelectionWithTheAccent(_list);
        _list.SelectionChanged += (_, _) => SelectionChanged?.Invoke(SelectedNode);
        _list.DoubleTapped += (_, _) => ActivateSelected();
        _list.ContextMenu = BuildContextMenu();
        // A right-click selects the row under the pointer first, so the context menu (Pin, Rename, …) always
        // targets what was clicked rather than whatever happened to be selected before.
        _list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);

        // The UserControl owns keyboard focus + the whole key map, so navigation is deterministic and
        // doesn't depend on which item container happens to hold focus.
        Focusable = true;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        Content = _list;
    }

    /// <summary>Raised when the selected entry changes (null when the selection is cleared).</summary>
    public event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Raised when a <em>file</em> entry is activated (double-click / Enter), to open it in a pane.</summary>
    public event Action<NotebookNode>? FileActivated;

    /// <summary>Raised to rename <paramref>node</paramref> (F2 / context menu). Never the notebook root.</summary>
    public event Action<NotebookNode>? RenameRequested;

    /// <summary>Raised to delete a node (Delete / context menu). Never the notebook root.</summary>
    public event Action<NotebookNode>? DeleteRequested;

    /// <summary>Raised to create a new Markdown note; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewNoteRequested;

    /// <summary>Raised to create a new spreadsheet; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewSpreadsheetRequested;

    /// <summary>Raised to create a new folder; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>Raised to pin a folder to the sidebar's chips (context menu). Always a directory.</summary>
    public event Action<NotebookNode>? PinRequested;

    /// <summary>Raised to unpin an already-pinned folder from the sidebar's chips (context menu).</summary>
    public event Action<NotebookNode>? UnpinRequested;

    /// <summary>The currently selected node, or null when nothing is selected.</summary>
    public NotebookNode? SelectedNode => (_list.SelectedItem as Row)?.Node;

    /// <summary>The names of the currently visible rows, top to bottom (test seam).</summary>
    internal IReadOnlyList<string> VisibleNamesForTest =>
        (_list.ItemsSource as IReadOnlyList<Row>)?.Select(r => r.Node.Name).ToList() ?? [];

    /// <summary>The selected row's name, or null (test seam).</summary>
    internal string? SelectedNameForTest => SelectedNode?.Name;

    /// <summary>Move keyboard focus into the tree (the shell's "focus file tree" command).</summary>
    public void FocusTree()
    {
        Focus();
        if (_list.SelectedIndex < 0 && _list.ItemCount > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    /// <summary>Rebuild the tree for a newly opened notebook (or clear it when <paramref name="root"/> is null).</summary>
    public void SetRoot(NotebookNode? root)
    {
        if (root is null)
        {
            _root = null;
            _expanded.Clear();
            Rebuild(selectPath: null);
            return;
        }

        SetScope(root);
    }

    /// <summary>
    /// Re-root the visible tree at <paramref name="displayRoot"/> — the notebook root for "All notes", or a
    /// pinned sub-folder when a chip scopes the tree (PRD §7.2). Expansion resets to just the new root.
    /// </summary>
    public void SetScope(NotebookNode displayRoot)
    {
        _root = displayRoot;
        _expanded.Clear();
        _expanded.Add(displayRoot.Path); // show the scoped level immediately
        Rebuild(selectPath: displayRoot.Path);
    }

    /// <summary>The paths currently pinned to the sidebar, so the context menu can offer Pin vs. Unpin.</summary>
    public void SetPinnedPaths(IReadOnlySet<string> pinned) => _pinned = pinned;

    /// <summary>Re-read the tree from disk, preserving expansion and (when it still exists) the selection.</summary>
    public void Refresh() => Rebuild(selectPath: SelectedNode?.Path);

    /// <summary>Expand the ancestors of <paramref name="path"/>, then select and reveal it.</summary>
    public void SelectPath(string path)
    {
        if (_root is null)
        {
            return;
        }

        // Expand every ancestor directory so the target becomes a visible row.
        var full = System.IO.Path.GetFullPath(path);
        for (var dir = System.IO.Path.GetDirectoryName(full); dir is not null; dir = System.IO.Path.GetDirectoryName(dir))
        {
            _expanded.Add(dir);
            if (string.Equals(dir, _root.Path, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        Rebuild(selectPath: full);
    }

    // ---- Rendering & flattening -------------------------------------------------------------------

    private void Rebuild(string? selectPath)
    {
        // The notebook root is never a row of its own — the sidebar header already names the open notebook
        // (and the breadcrumb names a scoped folder), so the tree shows only what's *inside* it, top-level
        // entries at depth 0. Its children are always visible because SetRoot/SetScope expand the root path.
        var rows = new List<Row>();
        if (_root is not null)
        {
            AppendChildren(_root, 0, rows);
        }

        _list.ItemsSource = rows;

        var index = selectPath is null ? -1 : rows.FindIndex(r => string.Equals(r.Node.Path, selectPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && rows.Count > 0)
        {
            index = 0;
        }

        _list.SelectedIndex = index;
        if (index >= 0)
        {
            _list.ScrollIntoView(index);
        }
    }

    private void AppendChildren(NotebookNode directory, int depth, List<Row> rows)
    {
        if (!_expanded.Contains(directory.Path))
        {
            return;
        }

        foreach (var child in directory.EnumerateChildren())
        {
            rows.Add(new Row(child, depth));
            if (child.IsDirectory)
            {
                AppendChildren(child, depth + 1, rows);
            }
        }
    }

    /// <summary>
    /// Paint row selection with the app's accent.
    ///
    /// <para>This list never styled its own <see cref="ListBoxItem"/>s, so its selected row fell through to
    /// FluentTheme's default system accent — <b>blue</b> — while every hand-built surface in the shell (the
    /// active tab, the nav item, the pinned chips) reads the orange accent from <see cref="Tokens"/>. The same
    /// state, "this is the current thing", was being said in two colours at once: the selected file showed blue
    /// in the tree and orange in its tab. That reads as an accident, not a system.</para>
    ///
    /// <para>The fix isn't a token change — it's that the token was never applied here at all.</para>
    /// </summary>
    private static void StyleSelectionWithTheAccent(ListBox list)
    {
        var palette = Tokens.Dark;

        static Style ItemBackground(Func<Selector?, Selector> selector, IBrush background) =>
            new(x => selector(x).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, background) },
            };

        // Selected (including when the list doesn't have focus — Avalonia drops :focus but keeps :selected).
        list.Styles.Add(ItemBackground(x => x.OfType<ListBoxItem>().Class(":selected"), palette.AccentSubtle));
        list.Styles.Add(ItemBackground(
            x => x.OfType<ListBoxItem>().Class(":selected").Class(":pointerover"), palette.AccentSubtle));
        list.Styles.Add(ItemBackground(
            x => x.OfType<ListBoxItem>().Class(":pointerover"), palette.SurfaceBorder));
    }

    private Control BuildRow(Row? row)
    {
        if (row is null)
        {
            return new Control();
        }

        var palette = Tokens.Dark;
        var expanded = _expanded.Contains(row.Node.Path);

        // A disclosure chevron, ahead of the folder glyph. The folder/folder-open icon swap alone was too weak
        // a signal to answer the question a tree has to answer at a glance: is this folder collapsed, or is it
        // empty? An *expandable* folder gets a chevron that turns; an empty one gets none, and so reads as
        // having nothing to open rather than as something the user failed to click. Files reserve the same slot
        // (drawn empty) so every name stays aligned under its siblings.
        var expandable = row.Node.IsDirectory && row.Node.EnumerateChildren().Count > 0;
        Control disclosure = expandable
            ? new LucideIcon
            {
                IconKey = expanded ? "chevron-down" : "chevron-right",
                Size = 12,
                Foreground = palette.MutedText,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new Control { Width = 12 };

        Control glyph = row.Node.IsDirectory
            ? new LucideIcon
            {
                IconKey = expanded ? "folder-open" : "folder",
                Size = 15,
                Foreground = palette.MutedText,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new Control { Width = 15 };

        var name = new TextBlock
        {
            Text = row.Node.Name,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = row.Node.IsDirectory ? FontWeight.Medium : FontWeight.Normal,
        };
        if (!row.Node.IsDirectory)
        {
            name.Foreground = palette.MutedText; // files read one step back from folders (which use the default text brush)
        }

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            MinHeight = 24, // a comfortable pointer target (WCAG 2.2 §2.5.8) — rows were as short as their text
            Margin = new Thickness(row.Depth * 14, 0, 0, 0),
            Children = { disclosure, glyph, name },
        };

        // Say the disclosure state out loud, not only in the chevron: a screen reader gets "collapsed"/"expanded"
        // rather than an unannotated row.
        AutomationProperties.SetName(stack, row.Node.Name);
        if (expandable)
        {
            AutomationProperties.SetHelpText(stack, expanded ? "expanded" : "collapsed");
        }

        return stack;
    }

    // ---- Keyboard -------------------------------------------------------------------------------

    private void ActivateSelected()
    {
        if (SelectedNode is { IsDirectory: false } file)
        {
            FileActivated?.Invoke(file);
        }
        else if (SelectedNode is { IsDirectory: true } dir)
        {
            Toggle(dir);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // The "Command" chord modifier is ⌘ on macOS, Ctrl elsewhere — matching the platform's editors and the
        // shell's own catalog.
        var command = UseMacKeymap ? KeyModifiers.Meta : KeyModifiers.Control;

        // Tree-scoped creation chords: ⌘N new note, ⌘⇧N new folder. These live on the tree (not the global
        // shell catalog) so they only fire while the tree holds focus — no collision with an editor's ⌘N, and
        // the shell picks the target folder from the current selection. Handled before the bare-key map below.
        if (e.Key == Key.N && e.KeyModifiers == command)
        {
            NewNoteRequested?.Invoke(SelectedNode);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N && e.KeyModifiers == (command | KeyModifiers.Shift))
        {
            NewFolderRequested?.Invoke(SelectedNode);
            e.Handled = true;
            return;
        }

        // Finder-style command chords macOS users reach for on a file list: ⌘↓ opens, ⌘↑ jumps to the enclosing
        // folder, ⌘⌫ moves to trash. Off macOS these modifiers are left alone for the framework.
        if (UseMacKeymap && e.KeyModifiers == command)
        {
            switch (e.Key)
            {
                case Key.Down:
                    ActivateSelected();
                    break;
                case Key.Up:
                    AscendToParent();
                    break;
                case Key.Back or Key.Delete:
                    RequestDelete();
                    break;
                default:
                    return; // other ⌘-chords aren't ours
            }

            e.Handled = true;
            return;
        }

        // The tree owns its whole key map (rather than leaning on the ListBox's container-focus-based
        // navigation) so selection moves deterministically whether or not an item container has focus. The
        // activation / delete keys split by platform (see UseMacKeymap); navigation is shared.
        switch (e.Key)
        {
            case Key.Enter:
                if (UseMacKeymap)
                {
                    RequestRename(); // Finder / Xcode / VS Code: Return renames the selection
                }
                else
                {
                    ActivateSelected();
                }

                break;
            case Key.Space when UseMacKeymap:
                ActivateSelected(); // opens the file / toggles the folder in the macOS map
                break;
            case Key.Right:
                MoveRight();
                break;
            case Key.Left:
                MoveLeft();
                break;
            case Key.Down:
                MoveSelection(+1);
                break;
            case Key.Up:
                MoveSelection(-1);
                break;
            case Key.Home:
                SelectIndex(0);
                break;
            case Key.End:
                SelectIndex(_list.ItemCount - 1);
                break;
            case Key.F2:
                RequestRename();
                break;
            // Off macOS a bare Delete/Backspace deletes; on macOS deletion is ⌘⌫ only, so a stray Delete is inert.
            case Key.Delete or Key.Back when !UseMacKeymap:
                RequestDelete();
                break;
            default:
                return; // leave other keys (Tab, macOS bare Delete, etc.) to the framework
        }

        e.Handled = true;
    }

    private bool IsRoot(NotebookNode node) => _root is { } r && string.Equals(node.Path, r.Path, StringComparison.OrdinalIgnoreCase);

    private void RequestRename()
    {
        if (SelectedNode is { } node && !IsRoot(node))
        {
            RenameRequested?.Invoke(node);
        }
    }

    private void RequestDelete()
    {
        if (SelectedNode is { } node && !IsRoot(node))
        {
            DeleteRequested?.Invoke(node);
        }
    }

    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string header, Action run)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => run();
            return item;
        }

        _pinItem = Item("Pin to Sidebar", () =>
        {
            if (SelectedNode is { IsDirectory: true } dir)
            {
                PinRequested?.Invoke(dir);
            }
        });
        _unpinItem = Item("Unpin from Sidebar", () =>
        {
            if (SelectedNode is { IsDirectory: true } dir)
            {
                UnpinRequested?.Invoke(dir);
            }
        });
        var pinSeparator = new Separator();

        var menu = new ContextMenu
        {
            ItemsSource = new List<Control>
            {
                _pinItem,
                _unpinItem,
                pinSeparator,
                Item("New Note…", () => NewNoteRequested?.Invoke(SelectedNode)),
                Item("New Spreadsheet…", () => NewSpreadsheetRequested?.Invoke(SelectedNode)),
                Item("New Folder…", () => NewFolderRequested?.Invoke(SelectedNode)),
                new Separator(),
                Item("Rename…", RequestRename),
                Item("Delete", RequestDelete),
            },
        };

        // Pin/Unpin only make sense on a folder, and are mutually exclusive by its current pinned state.
        menu.Opening += (_, _) =>
        {
            bool isFolder = SelectedNode is { IsDirectory: true };
            bool pinned = isFolder && _pinned.Contains(SelectedNode!.Path);
            _pinItem.IsVisible = isFolder && !pinned;
            _unpinItem.IsVisible = isFolder && pinned;
            pinSeparator.IsVisible = isFolder;
        };
        return menu;
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsRightButtonPressed)
        {
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is { DataContext: Row row })
        {
            _list.SelectedItem = row;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_list.ItemCount == 0)
        {
            return;
        }

        int index = Math.Clamp((_list.SelectedIndex < 0 ? 0 : _list.SelectedIndex) + delta, 0, _list.ItemCount - 1);
        SelectIndex(index);
    }

    private void SelectIndex(int index)
    {
        if (index < 0 || index >= _list.ItemCount)
        {
            return;
        }

        _list.SelectedIndex = index;
        _list.ScrollIntoView(index);
    }

    private void MoveRight()
    {
        if (SelectedNode is not { IsDirectory: true } dir)
        {
            return;
        }

        if (_expanded.Add(dir.Path))
        {
            Rebuild(selectPath: dir.Path); // was collapsed → expand in place
        }
        else // already expanded → descend to the first child
        {
            SelectIndex(_list.SelectedIndex + 1);
        }
    }

    private void MoveLeft()
    {
        if (SelectedNode is { IsDirectory: true } dir && _expanded.Contains(dir.Path))
        {
            _expanded.Remove(dir.Path);
            Rebuild(selectPath: dir.Path); // collapse an open folder in place
            return;
        }

        // A file, or a collapsed folder → ascend to the parent row.
        AscendToParent();
    }

    /// <summary>Select the enclosing folder — the nearest shallower row above the selection (Left / ⌘↑).</summary>
    private void AscendToParent()
    {
        if (_list.ItemsSource is not IReadOnlyList<Row> rows || _list.SelectedIndex < 0)
        {
            return;
        }

        var depth = rows[_list.SelectedIndex].Depth;
        for (var i = _list.SelectedIndex - 1; i >= 0; i--)
        {
            if (rows[i].Depth < depth)
            {
                SelectIndex(i);
                return;
            }
        }
    }

    private void Toggle(NotebookNode dir)
    {
        if (!_expanded.Remove(dir.Path))
        {
            _expanded.Add(dir.Path);
        }

        Rebuild(selectPath: dir.Path);
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        var now = DateTime.UtcNow;
        _typeahead = now - _lastKeystroke > TypeaheadWindow ? e.Text : _typeahead + e.Text;
        _lastKeystroke = now;

        if (_list.ItemsSource is not IReadOnlyList<Row> rows || rows.Count == 0)
        {
            return;
        }

        // Search from just after the current selection, wrapping — prefix first, then a contains fallback.
        int start = _list.SelectedIndex + 1;
        int? match = FindMatch(rows, start, n => n.StartsWith(_typeahead, StringComparison.OrdinalIgnoreCase))
                  ?? FindMatch(rows, start, n => n.Contains(_typeahead, StringComparison.OrdinalIgnoreCase));
        if (match is { } index)
        {
            _list.SelectedIndex = index;
            _list.ScrollIntoView(index);
        }

        e.Handled = true;
    }

    private static int? FindMatch(IReadOnlyList<Row> rows, int start, Func<string, bool> predicate)
    {
        for (var offset = 0; offset < rows.Count; offset++)
        {
            int i = ((start + offset) % rows.Count + rows.Count) % rows.Count;
            if (predicate(rows[i].Node.Name))
            {
                return i;
            }
        }

        return null;
    }
}
