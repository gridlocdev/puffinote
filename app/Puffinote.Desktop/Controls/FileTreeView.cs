using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
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
///   to rename, pin, or delete it without disturbing what you're reading.</item>
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
    private IReadOnlySet<string> _pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        // A right-click selects the row under the pointer first, so the context menu (Pin, Rename, …) always
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

    /// <summary>Raised to create a new folder; the argument is the selected node (the shell picks the folder).</summary>
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>Raised to pin a folder to the sidebar's chips (context menu). Always a directory.</summary>
    public event Action<NotebookNode>? PinRequested;

    /// <summary>Raised to unpin an already-pinned folder from the sidebar's chips (context menu).</summary>
    public event Action<NotebookNode>? UnpinRequested;

    /// <summary>Raised to show a file or folder in the host OS's file manager (context menu, PRD §7.2).</summary>
    public event Action<NotebookNode>? RevealRequested;

    /// <summary>Raised to open a file Puffinote has no editor for in its default app (context menu, PRD §7.2).</summary>
    public event Action<NotebookNode>? OpenExternallyRequested;

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

    /// <summary>
    /// The paths currently pinned to the sidebar, so the context menu can offer Pin vs. Unpin — and so each
    /// folder row's star knows whether to draw solid or hollow. A changed set repaints the rows; an unchanged one
    /// is a no-op, because this is re-asserted on every chip render, including ones that changed nothing.
    /// </summary>
    public void SetPinnedPaths(IReadOnlySet<string> pinned)
    {
        if (_pinned.SetEquals(pinned))
        {
            return;
        }

        _pinned = pinned;
        Rebuild(selectPath: SelectedNode?.Path);
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
    /// <para>The selected row also carries an accent <b>bar</b> down its leading edge, not just the wash —
    /// a second, non-colour channel for "you are here" (WCAG 2.2 §1.4.1), spelled the same way as the sidebar's
    /// active nav underline. The bar's width is reserved (transparent) on every row so selecting one doesn't
    /// shove its name two pixels sideways.</para>
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
        list.Styles.Add(Item(
            x => x.OfType<ListBoxItem>(),
            new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(6)),
            new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(SelectionBarWidth, 0, 0, 0)),
            new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent),
            new Setter(ContentPresenter.PaddingProperty, new Thickness(0))));

        list.Styles.Add(Item(
            x => x.OfType<ListBoxItem>().Class(":pointerover"),
            new Setter(ContentPresenter.BackgroundProperty, palette.SurfaceHover)));

        // Selected (including when the list doesn't have focus — Avalonia drops :focus but keeps :selected).
        foreach (var selected in new Func<Selector?, Selector>[]
                 {
                     x => x.OfType<ListBoxItem>().Class(":selected"),
                     x => x.OfType<ListBoxItem>().Class(":selected").Class(":pointerover"),
                 })
        {
            list.Styles.Add(Item(
                selected,
                new Setter(ContentPresenter.BackgroundProperty, palette.AccentSubtle),
                new Setter(ContentPresenter.BorderBrushProperty, palette.Accent)));
        }

        // The container itself carries the row's outer spacing; the ListBoxItem's own default padding would
        // otherwise inset the content *inside* the rounded, accent-barred presenter.
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(4, 1)),
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

        // A file shows its stem; its extension moves into the pill on the right, where the eye can scan a whole
        // column of types at once instead of hunting for a suffix at the ragged end of each name.
        var (stem, extension) = row.Node.IsDirectory
            ? (row.Node.Name, string.Empty)
            : FileGlyph.Split(row.Node.Name);

        var name = new TextBlock
        {
            Text = stem,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = row.Node.IsDirectory ? FontWeight.SemiBold : FontWeight.Normal,
        };
        if (!row.Node.IsDirectory)
        {
            // Files read one step back from folders, which keep the inherited default text brush. Assigning
            // `null` here to mean "use the default" would instead set a local null brush — and paint nothing.
            name.Foreground = palette.MutedText;
        }

        var content = new DockPanel
        {
            LastChildFill = true,
            MinHeight = RowHeight,

            // Transparent, not null: a panel with no background is not hit-testable in Avalonia, so the pointer
            // would sail straight through this row and the PointerEntered/Exited below would never fire — leaving
            // the pin star invisible forever on an unpinned folder. Transparent still paints nothing, so the
            // ListBoxItem's own hover/selection wash (drawn behind it) shows through unchanged.
            Background = Brushes.Transparent,
        };
        content.Children.Add(Dock(BuildGuides(row, palette), Avalonia.Controls.Dock.Left));
        content.Children.Add(Dock(disclosure, Avalonia.Controls.Dock.Left));
        content.Children.Add(Dock(glyph, Avalonia.Controls.Dock.Left));

        // Docked right, before the name, so the name is what gives way (ellipsizes) when the sidebar narrows —
        // never the type or the pin, which are fixed-width and carry the row's whole meaning at a glance.
        if (row.Node.IsDirectory)
        {
            content.Children.Add(Dock(BuildPinStar(row, palette), Avalonia.Controls.Dock.Right));
        }
        else if (extension.Length > 0)
        {
            content.Children.Add(Dock(BuildTypePill(extension, palette), Avalonia.Controls.Dock.Right));
        }

        content.Children.Add(name);

        // Say the disclosure state out loud, not only in the chevron: a screen reader gets "collapsed"/"expanded"
        // rather than an unannotated row. The name is announced whole (with its extension) even though the row
        // splits it in two visually — "Roadmap, md" is not what a file is called.
        AutomationProperties.SetName(content, row.Node.Name);
        if (expandable)
        {
            AutomationProperties.SetHelpText(content, expanded ? "expanded" : "collapsed");
        }

        // The pin star is revealed by hovering the row (it is always visible once the folder *is* pinned).
        if (row.Node.IsDirectory)
        {
            var star = content.Children.OfType<Button>().First();
            content.PointerEntered += (_, _) => star.Opacity = 1;
            content.PointerExited += (_, _) => star.Opacity = _pinned.Contains(row.Node.Path) ? 1 : 0;
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

    /// <summary>The file's type, spelled out — the channel that survives a reader who cannot tell the glyph colours apart.</summary>
    private static Control BuildTypePill(string extension, ThemePalette palette) => new Border
    {
        Child = new TextBlock
        {
            Text = extension.ToUpperInvariant(),
            FontSize = 9,
            LetterSpacing = 0.5,
            FontWeight = FontWeight.Medium,
            Foreground = palette.MutedText,
        },
        Background = palette.SurfaceInset,
        BorderBrush = palette.SurfaceBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(5, 1, 5, 2),
        Margin = new Thickness(6, 0, 2, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// Pin a folder to the sidebar's chips straight from its row. Pinning was previously reachable only through
    /// the context menu — discoverable if you already knew, invisible if you didn't.
    ///
    /// <para>A pinned folder's star is <b>solid</b>, an unpinned one's is an outline, so the state is not carried
    /// by the accent colour alone (WCAG 2.2 §1.4.1). It is kept out of the tab order: the tree owns its key map,
    /// and the context menu is the keyboard route to the same two commands.</para>
    /// </summary>
    private Button BuildPinStar(Row row, ThemePalette palette)
    {
        bool pinned = _pinned.Contains(row.Node.Path);
        var star = new Button
        {
            Content = new LucideIcon
            {
                IconKey = "star",
                Size = 13,
                IsFilled = pinned,
                Foreground = pinned ? palette.Accent : palette.MutedText,
            },
            Width = 24, // WCAG 2.2 §2.5.8's floor; the row is 26px, so the full target fits
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 2, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsTabStop = false,
            Opacity = pinned ? 1 : 0, // revealed on row hover; see BuildRow
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        star.Click += (_, _) =>
        {
            if (pinned)
            {
                UnpinRequested?.Invoke(row.Node);
            }
            else
            {
                PinRequested?.Invoke(row.Node);
            }
        };

        ToolTip.SetTip(star, pinned ? "Unpin from sidebar" : "Pin to sidebar");
        AutomationProperties.SetName(star, pinned ? $"Unpin {row.Node.Name}" : $"Pin {row.Node.Name}");
        return star;
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
    ///   <item><b>Folder</b> — expand/collapse it, create inside it, pin it to the sidebar, reveal it, rename or
    ///   delete it. A folder is a <em>container</em>, so it is the only row where "New …" appears: what you get is
    ///   what the header says, an entry inside the folder you clicked.</item>
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
            items.Add(new Separator());
        }

        items.Add(Item("New Note…", () => NewNoteRequested?.Invoke(folder)));
        items.Add(Item("New Spreadsheet…", () => NewSpreadsheetRequested?.Invoke(folder)));
        items.Add(Item("New Folder…", () => NewFolderRequested?.Invoke(folder)));
        items.Add(new Separator());

        items.Add(_pinned.Contains(folder.Path)
            ? Item("Unpin from Sidebar", () => UnpinRequested?.Invoke(folder))
            : Item("Pin to Sidebar", () => PinRequested?.Invoke(folder)));
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
        Item("New Note…", () => NewNoteRequested?.Invoke(_root)),
        Item("New Spreadsheet…", () => NewSpreadsheetRequested?.Invoke(_root)),
        Item("New Folder…", () => NewFolderRequested?.Invoke(_root)),
        new Separator(),
        Item("Collapse All", CollapseAll),
    ];

    /// <summary>
    /// Decide, at press time, what the click that follows will do.
    ///
    /// <para>A <b>right</b> click just selects the row under the pointer, so the context menu always targets what
    /// was clicked. A plain <b>left</b> click selects <i>and</i> activates — opening a file, toggling a folder —
    /// because in a notes sidebar the overwhelmingly common reason to click a note is to read it, and making that
    /// cost two clicks taxes every single navigation. Holding the platform's command modifier
    /// (<b>⌘</b> on macOS, <b>Ctrl</b> elsewhere — see <see cref="UseMacKeymap"/>) selects without activating: the
    /// escape hatch for targeting a file to rename, pin, or delete without opening it first.</para>
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

        // The pin star is a button living inside the row. Its own Click does the work, and the row must not
        // *also* open the file / toggle the folder underneath it.
        if ((e.Source as Visual)?.FindAncestorOfType<Button>() is not null)
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
