using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Panes;
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
/// The mouse map is a notes sidebar's, not a file manager's:
/// <list type="bullet">
///   <item>A <b>single click</b> opens a file / toggles a folder. The reason to click a note is almost always to
///   read it, so charging two clicks for it taxes every navigation in the app.</item>
///   <item><b>⌘+click</b> (macOS) / <b>Ctrl+click</b> (elsewhere) selects without opening — how you target a file
///   to rename or delete it without disturbing what you're reading.</item>
///   <item>A <b>right click</b> selects the row under the pointer, so the context menu targets what was clicked
///   — and lands on a <em>folder</em>, a <em>file</em>, or the empty space below the rows, each of which gets its
///   own menu (see <see cref="BuildContextMenu"/>).</item>
/// </list>
/// The <see cref="NotebookNode"/> model is unchanged; mutation actions (new/rename/delete) drive this through
/// <see cref="Refresh"/> / <see cref="SelectPath"/>.
/// </summary>
internal sealed class FileTreeView : UserControl
{
    private sealed record Row(NotebookNode Node, int Depth);

    private static readonly TimeSpan TypeaheadWindow = TimeSpan.FromSeconds(1);

    /// <summary>Width of one level of nesting — and so the spacing of the indent guides.</summary>
    private const double IndentStep = 14;

    /// <summary>The accent bar down the leading edge of the selected row (reserved on every row).</summary>
    private const double SelectionBarWidth = 2;

    /// <summary>Comfortably above WCAG 2.2 §2.5.8's 24px floor for a pointer target.</summary>
    private const double RowHeight = 26;

    /// <summary>Names the indent-guide borders so <see cref="RefreshGuides"/> can find them in a realized row.</summary>
    private const string GuideName = "PART_TreeGuide";

    private readonly ListBox _list;
    private NotebookNode? _root;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private string? _activePath;
    private string _typeahead = string.Empty;
    private DateTime _lastKeystroke = DateTime.MinValue;

    /// <summary>Whether the click currently in flight should activate its row, decided at press time from the
    /// button and modifiers (a plain left click activates; ⌘/Ctrl+click only selects).</summary>
    private bool _activateOnTap;

    /// <summary>The row a select-only (⌘/Ctrl) click landed on, forced back into the selection once the ListBox
    /// has finished with the event — see <see cref="OnListTapped"/>.</summary>
    private Row? _selectOnlyRow;

    /// <summary>
    /// Whether the bare-key map follows macOS editor conventions (Return renames, Space/⌘↓ opens, ⌘↑ ascends,
    /// ⌘⌫ deletes) rather than the Windows/Linux one (Enter opens, Delete deletes). Defaults to the running OS;
    /// a test seam so either map can be exercised on the headless Linux CI.
    /// </summary>
    internal bool UseMacKeymap { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>
    /// Whether a file row spells its extension out after its name (see <see cref="BuildRow"/>). On, the tree reads
    /// as a folder of files — <c>Roadmap.md</c>, the whole name, the way it is everywhere else in the OS. Off, it
    /// reads as a list of notes, and the glyph is left to say what each one is.
    ///
    /// <para>Which of those you want is a matter of taste rather than a right answer, so it is a preference — and
    /// a session-only command besides, for the moment you need to see the suffixes on a notebook you normally read
    /// without them. Assigning an unchanged value is a no-op: this is re-asserted whenever settings resolve, which
    /// includes opening a notebook, and a repaint there would drop the tree's scroll position for nothing.</para>
    /// </summary>
    public bool ShowExtensions
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Rebuild(selectPath: SelectedNode?.Path);
        }
    } = true;

    public FileTreeView()
    {
        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = false, // keyboard focus lives on the UserControl (below); the list is pointer-driven only
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => BuildRow(row), supportsRecycling: true),
        };
        StyleRows(_list);
        _list.SelectionChanged += (_, _) =>
        {
            RefreshGuides(); // the traced branch follows the selection
            SelectionChanged?.Invoke(SelectedNode);
        };
        _list.ContextMenu = BuildContextMenu();
        // A right-click selects the row under the pointer first, so the context menu (Enter Folder, Rename, …) always
        // targets what was clicked rather than whatever happened to be selected before. The same handler decides
        // whether the click that follows should also *activate* the row (see OnListPointerPressed).
        _list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        // The select-only re-assert rides PointerReleased rather than Tapped because Tapped does *not* fire for
        // the second click of a double-click (that raises DoubleTapped) — so a quick ⌘/Ctrl double-click on one
        // row would otherwise leave the selection cleared by Avalonia's toggle handling. Released always fires.
        _list.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.Tapped += OnListTapped;

        // The UserControl owns keyboard focus + the whole key map, so navigation is deterministic and
        // doesn't depend on which item container happens to hold focus.
        Focusable = true;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        Content = _list;
    }

    /// <summary>Raised when the selected entry changes (null when the selection is cleared).</summary>
    public event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Raised when a <em>file</em> entry is activated (a single click, or Enter), to open it in a pane.</summary>
    public event Action<NotebookNode>? FileActivated;

    /// <summary>Raised to rename <paramref>node</paramref> (F2 / context menu). Never the notebook root.</summary>
    public event Action<NotebookNode>? RenameRequested;

    /// <summary>Raised to delete a node (Delete / context menu). Never the notebook root.</summary>
    public event Action<NotebookNode>? DeleteRequested;

    /// <summary>Raised to create a new Markdown note; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewNoteRequested;

    /// <summary>Raised to create a new spreadsheet; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewSpreadsheetRequested;

    /// <summary>Raised to create a new task board; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewTaskboardRequested;

    /// <summary>Raised to create a file of any other type; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewFileRequested;

    /// <summary>Raised to create a new folder; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>Raised to narrow the sidebar to a folder's contents (context menu, PRD §7.2). Always a directory.</summary>
    public event Action<NotebookNode>? EnterFolderRequested;

    /// <summary>Raised to show a file or folder in the host OS's file manager (context menu, PRD §7.2).</summary>
    public event Action<NotebookNode>? RevealRequested;

    /// <summary>Raised to open a file Puffinote has no editor for in its default app (context menu, PRD §7.2).</summary>
    public event Action<NotebookNode>? OpenExternallyRequested;

    /// <summary>
    /// Raised whenever the tree re-renders, so the sidebar's expand/collapse toggle can re-read
    /// <see cref="AnyFolderExpanded"/> and re-label itself. Every route into a changed expansion state ends in a
    /// rebuild — the header button, the context menu, a click on a twisty, Left/Right, revealing the active
    /// document — so listening here catches all of them rather than only the ones the header started.
    /// </summary>
    public event Action? ExpansionChanged;

    /// <summary>
    /// Whether any folder in the tree is open and showing its contents — false exactly when the tree is fully
    /// collapsed, which is what makes the header's toggle read "Expand all folders" instead. A folder with
    /// nothing in it doesn't count: opening one changes nothing on screen, and a button that claims there is
    /// something to collapse when the tree plainly shows there isn't reads as broken.
    ///
    /// <para>Both this and <see cref="HasExpandableFolder"/> are recomputed by each <see cref="Rebuild"/> from
    /// the rows it just laid out.</para>
    /// </summary>
    public bool AnyFolderExpanded { get; private set; }

    /// <summary>
    /// Whether the tree holds a folder with anything in it — false for a flat notebook, where neither collapsing
    /// nor expanding has anything to act on and the toggle is disabled rather than dead to the click.
    /// </summary>
    public bool HasExpandableFolder { get; private set; }

    /// <summary>The currently selected node, or null when nothing is selected.</summary>
    public NotebookNode? SelectedNode => (_list.SelectedItem as Row)?.Node;

    /// <summary>
    /// Where a new entry goes: the selection, or — with nothing selected — the folder the tree is currently
    /// rooted at, so a creation with no selection lands in the scope you are looking at rather than back at the
    /// notebook root. (The shell then puts it inside a selected folder, or beside a selected file.)
    /// </summary>
    private NotebookNode? CreationContext => SelectedNode ?? _root;

    /// <summary>The names of the currently visible rows, top to bottom (test seam).</summary>
    internal IReadOnlyList<string> VisibleNamesForTest =>
        (_list.ItemsSource as IReadOnlyList<Row>)?.Select(r => r.Node.Name).ToList() ?? [];

    /// <summary>The selected row's name, or null (test seam).</summary>
    internal string? SelectedNameForTest => SelectedNode?.Name;

    /// <summary>The headers the context menu would show for the current selection, separators as "—" (test seam).</summary>
    internal IReadOnlyList<string> ContextMenuHeadersForTest =>
        [.. MenuFor(SelectedNode).Select(c => c is MenuItem item ? item.Header as string ?? string.Empty : "—")];

    /// <summary>Click a context-menu item of the current selection's menu by its header (test seam).</summary>
    internal void InvokeContextMenuItemForTest(string header) =>
        MenuFor(SelectedNode)
            .OfType<MenuItem>()
            .First(item => string.Equals(item.Header as string, header, StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    /// <summary>Drop the selection, as a right click on the tree's empty space does (test seam).</summary>
    internal void ClearSelectionForTest() => _list.SelectedItem = null;

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
    /// Re-root the visible tree at <paramref name="displayRoot"/> — the notebook root for the whole notebook, or a
    /// sub-folder once the user enters it (PRD §7.2). Expansion resets to just the new root.
    /// </summary>
    public void SetScope(NotebookNode displayRoot)
    {
        _root = displayRoot;
        _expanded.Clear();
        _expanded.Add(displayRoot.Path); // show the scoped level immediately
        Rebuild(selectPath: displayRoot.Path);
    }

    /// <summary>
    /// The document currently open in the active tab (null when no tab is open) — the one row the tree draws in
    /// bold. This is <em>not</em> the tree's selection: you can select a row without opening it (⌘/Ctrl+click), and
    /// you can open a document without ever touching the tree (Quick Open, Recent, Search, ⌘1, Ctrl+Tab). Those
    /// are different questions — "what will Rename act on?" vs. "what am I reading?" — so they get different
    /// marks: the accent bar and wash for the selection, weight for the active document.
    ///
    /// <para>Switching tabs also <em>reveals</em> the document: its folders are opened and the tree scrolls it to
    /// the middle of the panel. A bold row is no use in a shut folder or 200px below the fold, and switching tabs
    /// is exactly when you have lost your place — so the sidebar re-answers "where am I?" rather than leaving the
    /// mark where the last click happened to put it.</para>
    ///
    /// <para>Re-asserted on every tab-host change, which includes every keystroke in the editor, so an unchanged
    /// path must stay a no-op rather than repaint the tree — and, more to the point, rather than yank the panel
    /// back to the active note every time you type while reading somewhere else in it.</para>
    /// </summary>
    public void SetActiveDocument(string? path)
    {
        var full = path is null ? null : System.IO.Path.GetFullPath(path);
        if (string.Equals(_activePath, full, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activePath = full;
        if (full is not null)
        {
            ExpandAncestors(full);
        }

        // The selection is left exactly where it was: it answers a different question (see above), and a tab
        // switch is not a claim about what Rename should act on.
        Rebuild(selectPath: SelectedNode?.Path);

        if (full is not null)
        {
            Reveal(full);
        }
    }

    /// <summary>Shut every folder, leaving the scope's top level (the sidebar's "Collapse all").</summary>
    public void CollapseAll()
    {
        if (_root is null)
        {
            return;
        }

        _expanded.Clear();
        _expanded.Add(_root.Path); // the scoped level itself stays open, or the tree would render empty
        Rebuild(selectPath: SelectedNode?.Path);
    }

    /// <summary>
    /// Open every folder in the scope, however deep (the sidebar's "Expand all") — the inverse of
    /// <see cref="CollapseAll"/>, and what its button offers once the tree is fully shut.
    ///
    /// <para>This walks the notebook rather than only the rows on screen: the point of the command is to reach
    /// the folders you <em>cannot</em> see, so stopping at the visible level would make it a no-op the second
    /// time you pressed it.</para>
    /// </summary>
    public void ExpandAll()
    {
        if (_root is null)
        {
            return;
        }

        _expanded.Add(_root.Path);
        ExpandDescendants(_root);
        Rebuild(selectPath: SelectedNode?.Path);
    }

    private void ExpandDescendants(NotebookNode directory)
    {
        foreach (var child in directory.EnumerateChildren())
        {
            if (child.IsDirectory)
            {
                _expanded.Add(child.Path);
                ExpandDescendants(child);
            }
        }
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

        var full = System.IO.Path.GetFullPath(path);
        ExpandAncestors(full);
        Rebuild(selectPath: full);
    }

    /// <summary>Open every folder between the scope root and <paramref name="fullPath"/>, so it becomes a row.</summary>
    private void ExpandAncestors(string fullPath)
    {
        if (_root is null)
        {
            return;
        }

        for (var dir = System.IO.Path.GetDirectoryName(fullPath); dir is not null; dir = System.IO.Path.GetDirectoryName(dir))
        {
            _expanded.Add(dir);
            if (string.Equals(dir, _root.Path, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Scroll <paramref name="fullPath"/>'s row to the middle of the panel — the position that shows the most of
    /// its neighbourhood, which is the point of looking at a tree at all. Near the ends of a long list there is
    /// nothing to centre against, so the offset clamps and the row simply sits in the first/last screenful;
    /// centring it there would mean scrolling past the end, into empty space.
    ///
    /// <para>The centring runs after layout: the list virtualizes, and the rebuild this follows has only just
    /// changed the items, so the row does not exist as a measurable container until the next pass.</para>
    /// </summary>
    private void Reveal(string fullPath)
    {
        if (_list.ItemsSource is not List<Row> rows)
        {
            return;
        }

        var index = rows.FindIndex(r => string.Equals(r.Node.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return; // not a row here — e.g. the tree is inside a folder the document doesn't live in
        }

        _list.ScrollIntoView(index); // realizes the container, so the posted pass below has something to measure
        Dispatcher.UIThread.Post(() => Centre(index), DispatcherPriority.Loaded);
    }

    private void Centre(int index)
    {
        if (_list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller
            || _list.ContainerFromIndex(index) is not { } row)
        {
            return;
        }

        var viewport = scroller.Viewport.Height;
        if (viewport <= 0)
        {
            return; // the sidebar is collapsed, or the Files panel isn't the one on screen
        }

        // Measured against the viewport rather than the panel, so this doesn't depend on how the virtualizing
        // panel positions realized rows: shift by however far the row's middle sits from the viewport's.
        if (row.TranslatePoint(default, scroller) is not { } origin)
        {
            return;
        }

        var target = scroller.Offset.Y + origin.Y + (row.Bounds.Height / 2) - (viewport / 2);
        var furthest = Math.Max(0, scroller.Extent.Height - viewport);
        scroller.Offset = scroller.Offset.WithY(Math.Clamp(target, 0, furthest));
    }

    // ---- Rendering & flattening -------------------------------------------------------------------

    private void Rebuild(string? selectPath)
    {
        // The notebook root is never a row of its own — the sidebar header already names the open notebook
        // (and the breadcrumb names a scoped folder), so the tree shows only what's *inside* it, top-level
        // entries at depth 0. Its children are always visible because SetRoot/SetScope expand the root path.
        var rows = new List<Row>();
        AnyFolderExpanded = false;
        if (_root is not null)
        {
            AppendChildren(_root, 0, rows);
        }

        // What the header's toggle keys off. "Is anything open?" falls out of the walk above for free. "Is there
        // anything to open?" needs a look inside the collapsed folders — but only until the first one that has
        // something in it, so in practice this is one directory read, and none at all once a folder is open.
        HasExpandableFolder = AnyFolderExpanded
            || rows.Any(r => r.Node.IsDirectory && r.Node.EnumerateChildren().Count > 0);

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

        ExpansionChanged?.Invoke();
    }

    /// <summary>
    /// Appends <paramref name="directory"/>'s visible descendants as rows. Returns whether it added any — which
    /// is what tells the caller this folder is not just flagged open but actually <em>showing</em> something, and
    /// so feeds <see cref="AnyFolderExpanded"/>.
    /// </summary>
    private bool AppendChildren(NotebookNode directory, int depth, List<Row> rows)
    {
        if (!_expanded.Contains(directory.Path))
        {
            return false;
        }

        var added = false;
        foreach (var child in directory.EnumerateChildren())
        {
            rows.Add(new Row(child, depth));
            added = true;
            if (child.IsDirectory && AppendChildren(child, depth + 1, rows))
            {
                AnyFolderExpanded = true;
            }
        }

        return added;
    }

    /// <summary>
    /// Mark the selected row with the app's accent — as a leading-edge <b>bar</b>, and only that.
    ///
    /// <para>This list never styled its own <see cref="ListBoxItem"/>s, so its selected row fell through to
    /// FluentTheme's default system accent — <b>blue</b> — while every hand-built surface in the shell (the
    /// active tab, the nav item, the entered-folder bar) reads the orange accent from <see cref="Tokens"/>. The same
    /// state, "this is the current thing", was being said in two colours at once: the selected file showed blue
    /// in the tree and orange in its tab. That reads as an accident, not a system.</para>
    ///
    /// <para><b>The wash belongs to the open document, not to the selection</b> (see <see cref="BuildRow"/>).
    /// Selection and "what is in the active tab" are different questions, and while both wore the same
    /// highlight the tree could answer them in two places at once: click a row, then switch tabs from the strip
    /// above, and two rows were washed — the one you last clicked and the one you were actually reading. The
    /// sidebar now highlights exactly the file behind the active tab, and nothing else ever wears that wash.</para>
    ///
    /// <para>Selection keeps the bar it already had — a non-colour channel for "the keyboard is here, this is
    /// what Rename acts on" (WCAG 2.2 §1.4.1), spelled the same way as the sidebar's active nav underline. The
    /// bar's width is reserved (transparent) on every row so selecting one doesn't shove its name two pixels
    /// sideways.</para>
    /// </summary>
    private static void StyleRows(ListBox list)
    {
        var palette = Tokens.Dark;

        static Style Item(Func<Selector?, Selector> selector, params Setter[] setters)
        {
            var style = new Style(x => selector(x).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            foreach (var setter in setters)
            {
                style.Setters.Add(setter);
            }

            return style;
        }

        // Resting: rounded, with the selected row's accent bar reserved but invisible.
        //
        // The 1px of air above and below each row's rounded surface is a margin on the *presenter*, not on the
        // ListBoxItem container (which carries only the horizontal inset, below). The two look identical — the
        // container measures 2px taller than its content either way — but a vertical margin on the container is
        // accumulated by the virtualizing panel: every time the tree's ItemsSource is replaced, one row's worth
        // of top margin was added to the extent and every row slid a pixel further down. Collapse all, refresh,
        // switch tabs — anything that rebuilds — and the tree crept downwards, a pixel a click, forever.
        list.Styles.Add(Item(
            x => x.OfType<ListBoxItem>(),
            new Setter(Layoutable.MarginProperty, new Thickness(0, 1)),
            new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(6)),
            new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(SelectionBarWidth, 0, 0, 0)),
            new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent),
            new Setter(ContentPresenter.PaddingProperty, new Thickness(0))));

        list.Styles.Add(Item(
            x => x.OfType<ListBoxItem>().Class(":pointerover"),
            new Setter(ContentPresenter.BackgroundProperty, palette.SurfaceHover)));

        // Selected (including when the list doesn't have focus — Avalonia drops :focus but keeps :selected).
        // The bar only: the wash is reserved for the open document, so the tree never highlights two rows.
        // Background is still set — to Transparent, and to the ordinary hover wash when hovered — because
        // leaving it unset doesn't mean "no wash"; it means falling back through to FluentTheme's ListBoxItem
        // theme, which is where the system-accent blue this method exists to displace comes from.
        list.Styles.Add(Item(
            x => x.OfType<ListBoxItem>().Class(":selected"),
            new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent),
            new Setter(ContentPresenter.BorderBrushProperty, palette.Accent)));

        list.Styles.Add(Item(
            x => x.OfType<ListBoxItem>().Class(":selected").Class(":pointerover"),
            new Setter(ContentPresenter.BackgroundProperty, palette.SurfaceHover),
            new Setter(ContentPresenter.BorderBrushProperty, palette.Accent)));

        // The container carries the row's horizontal inset only — the vertical pair lives on the presenter (see
        // above). The ListBoxItem's own default padding would otherwise inset the content *inside* the rounded,
        // accent-barred presenter.
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(4, 0)),
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
            },
        });
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

        // The glyph says what the row *is*: one folder colour for every directory, and for a file the glyph and
        // colour of its type (see FileGlyph). A blank spacer used to sit here for files.
        var kind = row.Node.IsDirectory ? default : FileGlyph.ForFile(row.Node.Name);
        var glyph = new LucideIcon
        {
            IconKey = row.Node.IsDirectory ? FileGlyph.FolderIcon(expanded) : kind.IconKey,
            Size = 15,
            Foreground = row.Node.IsDirectory ? FileGlyph.Folder : kind.Color,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 7, 0),
        };

        // A file's name is written whole — the stem in the primary ink, the extension trailing it in the muted
        // one, as one continuous label. The extension used to sit in a pill pinned to the row's right edge, which
        // aligned the types into a scannable column but at a real cost: it broke a file's name in two and put the
        // halves at opposite ends of the row, so nothing on screen was ever called what the file is called.
        // Muting the suffix in place keeps type legible at a glance and gives the name back.
        var (stem, extension) = row.Node.IsDirectory
            ? (row.Node.Name, string.Empty)
            : FileGlyph.Split(row.Node.Name);

        // Colour alone marks the document you are reading. Every row keeps the same weight: bold changes a
        // row's width, so the one signal that moves as you switch notes was also the one that nudged its
        // neighbours sideways. Folders and files read at the same level — the chevron and folder glyph already
        // say "folder", so a folder needs no extra ink to be told apart from a note.
        bool isActive = !row.Node.IsDirectory && string.Equals(row.Node.Path, _activePath, StringComparison.OrdinalIgnoreCase);
        var name = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = FontWeight.Normal,

            // The primary ink, which is the point: a panel whose whole job is to list names wrote every one of
            // them in the brush meant for secondary text, so the tree's own content was the dimmest thing in the
            // window. Assigning `null` to mean "inherit the default" would instead set a local null brush — and
            // paint nothing.
            Foreground = palette.Text,
        };

        // Two runs rather than two controls: trimming has to act on the name and its suffix as one string, or a
        // narrow sidebar would ellipsize the name against a suffix that never gives way. The extension is dropped
        // entirely — not merely dimmed — when the preference is off, so the row is the bare name.
        var inlines = new InlineCollection { new Run(stem) };
        if (ShowExtensions && extension.Length > 0)
        {
            inlines.Add(new Run("." + extension) { Foreground = palette.MutedText });
        }

        name.Inlines = inlines;

        var content = new DockPanel
        {
            LastChildFill = true,
            MinHeight = RowHeight,

            // The one wash in the tree, and it belongs to the document behind the active tab — exactly one row
            // is highlighted, whichever way you got there, from the tree or from the tab strip. Selection wears
            // the leading-edge bar instead (see StyleRows); when both were washes, clicking a row and then
            // switching tabs above lit two of them, and the sidebar stopped answering "which note am I in?".
            //
            // Painted here rather than by moving the ListBox selection, because selection answers a different
            // question (what Rename acts on, where the keyboard is). Merging them would drop this highlight the
            // moment you clicked a folder, which is exactly when you still want to see where you were.
            //
            // Transparent otherwise, never null: a panel with no background is not hit-testable in Avalonia, so
            // the pointer would sail straight through this row and the hover wash would never follow it.
            // Transparent still paints nothing, so the ListBoxItem's own hover wash (drawn behind it) shows
            // through unchanged.
            Background = isActive ? palette.AccentSubtle : Brushes.Transparent,
        };
        content.Children.Add(Dock(BuildGuides(row, palette), Avalonia.Controls.Dock.Left));
        content.Children.Add(Dock(disclosure, Avalonia.Controls.Dock.Left));
        content.Children.Add(Dock(glyph, Avalonia.Controls.Dock.Left));

        content.Children.Add(name);

        // Say the disclosure state out loud, not only in the chevron: a screen reader gets "collapsed"/"expanded"
        // rather than an unannotated row. The name is announced whole, extension included, however the row is
        // currently drawing it — hiding the suffix is a visual preference, and a file is still called what it is
        // called.
        AutomationProperties.SetName(content, row.Node.Name);
        if (expandable)
        {
            AutomationProperties.SetHelpText(content, expanded ? "expanded" : "collapsed");
        }
        else if (isActive)
        {
            // The wash is the only visual mark left now that weight says nothing, so the state has to be spoken
            // too — a screen reader would otherwise get an ordinary row (WCAG 2.2 §1.4.1). Folders are never
            // active, so this can't collide with the disclosure state above.
            AutomationProperties.SetHelpText(content, "open");
        }

        return content;
    }

    /// <summary>
    /// The vertical indent guides to the left of a nested row — one per level of ancestry, each tracing the
    /// branch of the folder that encloses it. Indentation alone tells you a row is nested but not <em>under
    /// what</em>: past two levels the eye has to travel back up a column of whitespace to find the parent. The
    /// guide draws that column in.
    ///
    /// <para>The guide of the branch the selection sits inside is drawn in the accent, so the path from the
    /// notebook down to the current file is traced rather than merely indented. That state moves with the
    /// selection, so it is repainted (not rebuilt) by <see cref="RefreshGuides"/>.</para>
    /// </summary>
    private Control BuildGuides(Row row, ThemePalette palette)
    {
        var guides = new StackPanel { Orientation = Orientation.Horizontal };
        for (var level = 0; level < row.Depth; level++)
        {
            var ancestor = AncestorPath(row, level);
            guides.Children.Add(new Border
            {
                Name = GuideName,
                Tag = ancestor,
                Width = IndentStep,
                BorderThickness = new Thickness(1, 0, 0, 0),
                BorderBrush = GuideBrush(ancestor, palette),
            });
        }

        return guides;
    }

    /// <summary>The accent when the selection lives inside <paramref name="ancestor"/>'s branch; a hairline otherwise.</summary>
    private IBrush GuideBrush(string? ancestor, ThemePalette palette) =>
        ancestor is not null && SelectedNode is { } selected && PathScope.IsUnderOrEqual(selected.Path, ancestor)
            ? TracedGuide(palette)
            // Deliberately not SurfaceBorder: a guide is decoration, and that token is a 3:1 *boundary*. Repeated
            // down every level of a deep tree it out-shouts the file names it exists to organise.
            : palette.SurfaceHover;

    /// <summary>
    /// The traced branch — the accent, but at about a third of its strength.
    ///
    /// <para>A deep selection lights a guide at <em>every</em> level above it, so at full accent a four-deep file
    /// grew four saturated bars down the tree's left edge and the trace became the loudest thing on a panel whose
    /// entire job is to list names. Dimmed, it reads as what it is — a tint you follow back to the root — rather
    /// than as a stack of rules competing with the content.</para>
    /// </summary>
    private static IBrush TracedGuide(ThemePalette palette) =>
        palette.Accent is ISolidColorBrush solid
            ? new ImmutableSolidColorBrush(solid.Color, 0.32)
            : palette.Accent;

    /// <summary>
    /// The folder enclosing <paramref name="row"/> at nesting <paramref name="level"/> (0 = a top-level folder of
    /// the current scope), or null if the row is not that deep.
    /// </summary>
    private string? AncestorPath(Row row, int level)
    {
        if (_root is null)
        {
            return null;
        }

        var relative = System.IO.Path.GetRelativePath(_root.Path, row.Node.Path);
        var segments = relative.Split(
            [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        // The row itself is the last segment, so an *ancestor* level must fall strictly before it.
        return level >= segments.Length - 1
            ? null
            : System.IO.Path.Combine([_root.Path, .. segments[..(level + 1)]]);
    }

    /// <summary>
    /// Re-trace the guides when the selection moves. Only the realized rows are touched (a screenful), and rows
    /// that scroll into view afterwards paint themselves correctly from <see cref="BuildRow"/>.
    /// </summary>
    private void RefreshGuides()
    {
        var palette = Tokens.Dark;

        // Ask the list for the rows it has actually realized rather than probing every index: this runs on every
        // arrow-key press, and a held Down key through a 10,000-note notebook would otherwise do 10,000
        // container lookups per keystroke to repaint the one screenful that exists.
        foreach (var container in _list.GetRealizedContainers())
        {
            foreach (var guide in container.GetVisualDescendants().OfType<Border>().Where(b => b.Name == GuideName))
            {
                guide.BorderBrush = GuideBrush(guide.Tag as string, palette);
            }
        }
    }

    private static T Dock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
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
            NewNoteRequested?.Invoke(CreationContext);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N && e.KeyModifiers == (command | KeyModifiers.Shift))
        {
            NewFolderRequested?.Invoke(CreationContext);
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

    /// <summary>
    /// The context menu, rebuilt at open time for whatever the right click landed on — a folder, a file, or the
    /// empty space below the last row. One menu listing every command was cheaper to build but made the tree lie:
    /// it offered "New Note…" on a note (which quietly created a sibling, not a child), "Rename…" and "Delete" on
    /// empty space (aimed at whatever happened to still be selected), and never offered the two commands a file
    /// row most obviously needs — open it, and show me where it lives.
    ///
    /// <para>So each kind gets the commands that make sense <em>for that kind</em>, and nothing else:</para>
    /// <list type="bullet">
    ///   <item><b>Folder</b> — expand/collapse it, enter it (narrowing the sidebar to its contents), create inside
    ///   it, reveal it, rename or delete it. A folder is a <em>container</em>, so it is the only row where "New …"
    ///   appears: what you get is what the header says, an entry inside the folder you clicked.</item>
    ///   <item><b>File</b> — open it (in a tab, or in the host's default app when Puffinote has no editor for its
    ///   type — PRD §7.2), reveal it, rename or delete it. A file contains nothing, so it creates nothing.</item>
    ///   <item><b>Empty space</b> — the current scope's own menu: create at the top level of what the tree is
    ///   showing, or collapse it all. No rename/delete, because nothing was clicked (the right click that opened
    ///   it cleared the selection — see <see cref="OnListPointerPressed"/>).</item>
    /// </list>
    /// </summary>
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            // Refilled through Items, not by swapping ItemsSource: a source assigned this late (inside Opening)
            // is not picked up, and the menu opens empty.
            menu.Items.Clear();
            foreach (var item in MenuFor(SelectedNode))
            {
                menu.Items.Add(item);
            }
        };
        return menu;
    }

    private static MenuItem Item(string header, Action run)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => run();
        return item;
    }

    private IReadOnlyList<Control> MenuFor(NotebookNode? target) => target switch
    {
        { IsDirectory: true } folder => FolderMenu(folder),
        { } file => FileMenu(file),
        _ => ScopeMenu(),
    };

    private List<Control> FolderMenu(NotebookNode folder)
    {
        var items = new List<Control>();

        // An empty folder has nothing to open, so it is offered no toggle — the same rule the row's disclosure
        // chevron follows (see BuildRow), said in words.
        if (folder.EnumerateChildren().Count > 0)
        {
            bool expanded = _expanded.Contains(folder.Path);
            items.Add(Item(expanded ? "Collapse" : "Expand", () => Toggle(folder)));
        }

        // Expanding shows a folder's contents *in place*; entering makes them the whole sidebar. Same
        // destination, different amount of context — so the two sit together, above everything that edits.
        items.Add(Item("Enter Folder", () => EnterFolderRequested?.Invoke(folder)));
        items.Add(new Separator());

        // The same five, in the same order, as the sidebar header's New button — one list of what a notebook
        // can hold, wherever you ask for it.
        items.AddRange(NewItems(folder));
        items.Add(new Separator());

        items.Add(Item(SystemFileManager.RevealLabel, () => RevealRequested?.Invoke(folder)));
        items.Add(new Separator());

        items.Add(Item("Rename…", RequestRename));
        items.Add(Item("Delete", RequestDelete));
        return items;
    }

    private List<Control> FileMenu(NotebookNode file) =>
    [
        EditorPaneFactory.CanOpen(file.Path)
            ? Item("Open", () => FileActivated?.Invoke(file))
            : Item("Open in Default App", () => OpenExternallyRequested?.Invoke(file)),
        Item(SystemFileManager.RevealLabel, () => RevealRequested?.Invoke(file)),
        new Separator(),
        Item("Rename…", RequestRename),
        Item("Delete", RequestDelete),
    ];

    /// <summary>The menu for the tree's empty space: it acts on the scope, which is the only thing that was clicked.</summary>
    private List<Control> ScopeMenu() =>
    [
        .. NewItems(_root),
        new Separator(),
        Item("Collapse All", CollapseAll),
    ];

    /// <summary>
    /// The "New …" block, creating inside <paramref name="target"/>: folder first, then the three document
    /// types the shell seeds, then the any-extension escape hatch.
    /// </summary>
    private List<Control> NewItems(NotebookNode? target) =>
    [
        Item("New Folder…", () => NewFolderRequested?.Invoke(target)),
        Item("New Note…", () => NewNoteRequested?.Invoke(target)),
        Item("New Spreadsheet…", () => NewSpreadsheetRequested?.Invoke(target)),
        Item("New Taskboard…", () => NewTaskboardRequested?.Invoke(target)),
        Item("New File…", () => NewFileRequested?.Invoke(target)),
    ];

    /// <summary>
    /// Decide, at press time, what the click that follows will do.
    ///
    /// <para>A <b>right</b> click just selects the row under the pointer, so the context menu always targets what
    /// was clicked. A plain <b>left</b> click selects <i>and</i> activates — opening a file, toggling a folder —
    /// because in a notes sidebar the overwhelmingly common reason to click a note is to read it, and making that
    /// cost two clicks taxes every single navigation. Holding the platform's command modifier
    /// (<b>⌘</b> on macOS, <b>Ctrl</b> elsewhere — see <see cref="UseMacKeymap"/>) selects without activating: the
    /// escape hatch for targeting a file to rename or delete without opening it first.</para>
    ///
    /// <para>The modifier is captured here rather than in the tap handler because Avalonia's tap events don't
    /// carry the keyboard state.</para>
    /// </summary>
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(_list).Properties;
        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        _activateOnTap = false;
        _selectOnlyRow = null;

        // On macOS Ctrl+click *is* a right click, so the platform's select-only modifier has to be ⌘ there —
        // and this branch runs first either way, so a Ctrl+click on a Mac opens the context menu as the OS
        // intends rather than being read as a select-only click.
        if (properties.IsRightButtonPressed)
        {
            // A right click *is* the target: on a row it selects that row, and on the empty space below the rows
            // it clears the selection — so the menu that follows is the scope's (New …), not a Rename/Delete
            // aimed at whichever row the user last happened to touch (see BuildContextMenu).
            _list.SelectedItem = row is { DataContext: Row hit } ? hit : null;
            return;
        }

        if (!properties.IsLeftButtonPressed || row is not { DataContext: Row clicked })
        {
            return;
        }

        var selectOnly = e.KeyModifiers.HasFlag(UseMacKeymap ? KeyModifiers.Meta : KeyModifiers.Control);
        if (selectOnly)
        {
            _selectOnlyRow = clicked;
        }
        else
        {
            _activateOnTap = true;
        }
    }

    /// <summary>
    /// Finish a select-only (⌘/Ctrl) click by <i>re-asserting</i> the selection.
    ///
    /// <para>Avalonia reads Ctrl as a <b>toggle</b> modifier, so Ctrl-clicking the already-selected row clears the
    /// selection — the exact opposite of what a select-only click is for. Rather than fight the ListBox's own
    /// selection logic mid-flight, let it run and put the selection back afterwards.</para>
    /// </summary>
    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_selectOnlyRow is not { } row)
        {
            return;
        }

        _selectOnlyRow = null;
        _list.SelectedItem = row;
    }

    /// <summary>Complete a plain left click by activating the row it landed on (see <see cref="OnListPointerPressed"/>).</summary>
    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (!_activateOnTap)
        {
            return;
        }

        _activateOnTap = false;
        ActivateSelected();
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
