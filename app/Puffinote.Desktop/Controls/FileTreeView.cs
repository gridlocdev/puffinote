using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
///   <item>Enter opens a file into a tab, or toggles a folder.</item>
///   <item>Type-to-search jumps to the next node whose name matches the typed prefix.</item>
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
    private string _typeahead = string.Empty;
    private DateTime _lastKeystroke = DateTime.MinValue;

    public FileTreeView()
    {
        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = false, // keyboard focus lives on the UserControl (below); the list is pointer-driven only
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => BuildRow(row), supportsRecycling: true),
        };
        _list.SelectionChanged += (_, _) => SelectionChanged?.Invoke(SelectedNode);
        _list.DoubleTapped += (_, _) => ActivateSelected();
        _list.ContextMenu = BuildContextMenu();

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
        _root = root;
        _expanded.Clear();
        if (root is not null)
        {
            _expanded.Add(root.Path); // show the notebook's top level immediately
        }

        Rebuild(selectPath: root?.Path);
    }

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
        var rows = new List<Row>();
        if (_root is not null)
        {
            rows.Add(new Row(_root, 0));
            AppendChildren(_root, 1, rows);
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

    private Control BuildRow(Row? row)
    {
        if (row is null)
        {
            return new Control();
        }

        var palette = Tokens.Dark;
        var chevron = new TextBlock
        {
            Text = row.Node.IsDirectory ? (_expanded.Contains(row.Node.Path) ? "▾" : "▸") : string.Empty,
            Width = 14,
            Foreground = palette.MutedText,
            VerticalAlignment = VerticalAlignment.Center,
        };

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
            Margin = new Thickness(row.Depth * 14, 0, 0, 0),
            Children = { chevron, name },
        };
        AutomationProperties.SetName(stack, row.Node.Name);
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
        // The tree owns its whole key map (rather than leaning on the ListBox's container-focus-based
        // navigation) so selection moves deterministically whether or not an item container has focus.
        switch (e.Key)
        {
            case Key.Enter:
                ActivateSelected();
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
            case Key.Delete or Key.Back:
                RequestDelete();
                break;
            default:
                return; // leave other keys (Tab, etc.) to the framework
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

        return new ContextMenu
        {
            ItemsSource = new List<Control>
            {
                Item("New Note…", () => NewNoteRequested?.Invoke(SelectedNode)),
                Item("New Spreadsheet…", () => NewSpreadsheetRequested?.Invoke(SelectedNode)),
                Item("New Folder…", () => NewFolderRequested?.Invoke(SelectedNode)),
                new Separator(),
                Item("Rename…", RequestRename),
                Item("Delete", RequestDelete),
            },
        };
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

        // A file, or a collapsed folder → ascend to the parent row (the nearest shallower row above).
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
