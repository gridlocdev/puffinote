using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Controls.Icons;
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
    /// <summary>
    /// The gap above the sidebar's collapse control, and above the collapsed-state handle that stands in for
    /// it — one number, read by both, because the button is meant to be the <em>same</em> button in both
    /// states. It is the button's own breathing room, not a row's padding: the handle has no header row to
    /// take padding from, so anything the header added on its own account moved the control on collapse.
    /// </summary>
    internal const double ChromeTopInset = 2;

    private readonly FilesPanel _files = new();

    /// <summary>The Filter panel: the same tree, narrowed to what you type (also the seam a test types into).</summary>
    internal FilterPanel FilterPanelForTest { get; } = new();

    private readonly SearchPanel _search = new();
    private readonly OutlinePanel _outline = new();
    private readonly RecentPanel _recent = new();
    private readonly List<ISidebarPanel> _panels;

    /// <summary>The two panels that are the file tree — everything the shell does to "the tree" reaches both.</summary>
    private readonly IReadOnlyList<IFileTreePanel> _trees;

    // Normal case, not caps. All-caps costs legibility — it flattens the word-shapes a reader scans by — and a
    // folder name is content, not a label. Letter-spacing and weight carry the "this is a header" job instead.
    // Long names ellipsize rather than overflowing the sidebar, with the full path on hover.
    private readonly TextBlock _header = new()
    {
        Text = "No notebook open",
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.3,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        Margin = new Thickness(4, 8, 8, 8),
    };

    /// <summary>The header's icon buttons — hidden until a notebook is open, since neither means anything without one.</summary>
    private readonly StackPanel _actions = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 2,
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        IsVisible = false,
    };
    private readonly StackPanel _switcher = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 2,
        Margin = new Thickness(8, 0, 8, 6),
    };
    private readonly Border _body = new();

    /// <summary>Carries the folder toggle's hover message while the toggle itself is disabled.</summary>
    private readonly Border _foldersToggleHost = new();
    private ISidebarPanel _active;

    /// <summary>The header New button's menu, kept so a test can read and click it without a popup window.</summary>
    private List<MenuItem> _newItems = [];

    public SidebarView()
    {
        // Files first, then the same tree with a filter box over it, then the three lists.
        _panels = [_files, FilterPanelForTest, _search, _outline, _recent];
        _trees = [_files, FilterPanelForTest];
        _active = _files;

        foreach (var tree in _trees)
        {
            Wire(tree);
        }

        _search.FileActivated += path => FileActivated?.Invoke(path);
        _recent.FileActivated += path => FileActivated?.Invoke(path);

        var palette = Tokens.Active;

        // The notebook's name is content, not a label for something else, so it is written in the primary ink
        // like the file names below it. Muted, the one line naming what you have open sat at the same level as
        // the "drag to reorder" hint.
        _header.Foreground = palette.Text;

        BuildSwitcher();
        FoldersToggle = BuildActions(palette);
        foreach (var tree in _trees)
        {
            tree.ExpansionChanged += RefreshFoldersToggle;
        }

        RefreshFoldersToggle();
        _body.Child = _active.View;

        // The collapse control leads the row, in the window's top-left corner — the same spot the handle takes
        // over once the sidebar is hidden, so the affordance never moves. It sits outside `_actions` because it
        // is meaningful with no notebook open (the other two aren't), and so it never hides itself away.
        //
        // Pinned to the top at ChromeTopInset rather than centred in the header row, because the handle that
        // replaces it has no header row to be centred in. Two different rules put the same button at two
        // different heights, and the difference showed as a hop every time the sidebar was hidden or shown.
        var collapse = IconButton("panel-left", "Hide sidebar", palette, () => ToggleRequested?.Invoke());
        collapse.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        collapse.Margin = new Thickness(6, ChromeTopInset, 0, 0);
        CollapseButtonForTest = collapse;

        // The notebook name takes the row; the actions sit at its right edge, where a title bar's controls go.
        var headerRow = new DockPanel { LastChildFill = true };
        headerRow.Children.Add(Dock(collapse, Avalonia.Controls.Dock.Left));
        headerRow.Children.Add(Dock(_actions, Avalonia.Controls.Dock.Right));
        headerRow.Children.Add(_header);

        Content = new DockPanel
        {
            Children =
            {
                Dock(headerRow, Avalonia.Controls.Dock.Top),
                Dock(_switcher, Avalonia.Controls.Dock.Top),
                _body,
            },
        };
    }

    /// <summary>
    /// Hand one file-tree panel's requests on to the shell. Said once and applied to both panels, because the
    /// Files panel and the Filter panel are the same tree: a menu entry that works in one and not the other
    /// would be a difference nobody could account for.
    /// </summary>
    private void Wire(IFileTreePanel tree)
    {
        tree.FileActivated += path => FileActivated?.Invoke(path);
        tree.SelectionChanged += node => SelectionChanged?.Invoke(node);
        tree.RenameRequested += node => RenameRequested?.Invoke(node);
        tree.DeleteRequested += node => DeleteRequested?.Invoke(node);
        tree.NewNoteRequested += node => NewNoteRequested?.Invoke(node);
        tree.NewSpreadsheetRequested += node => NewSpreadsheetRequested?.Invoke(node);
        tree.NewTaskboardRequested += node => NewTaskboardRequested?.Invoke(node);
        tree.NewFileRequested += node => NewFileRequested?.Invoke(node);
        tree.NewFolderRequested += node => NewFolderRequested?.Invoke(node);
        tree.MoveRequested += (node, directory) => MoveRequested?.Invoke(node, directory);
        tree.RevealRequested += node => RevealRequested?.Invoke(node);
        tree.OpenExternallyRequested += node => OpenExternallyRequested?.Invoke(node);
    }

    /// <summary>Raised when a file is activated in any panel (tree or recent), to open it in a tab.</summary>
    public event Action<string>? FileActivated;

    /// <summary>Raised when the file-tree selection changes (for command-enablement refresh).</summary>
    public event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Raised when a panel becomes active (so the shell can refresh e.g. the outline on view).</summary>
    public event Action<string>? PanelActivated;

    /// <summary>Raised by the header's collapse control; the shell owns the layout, so it does the hiding.</summary>
    public event Action? ToggleRequested;

    /// <summary>Raised by the switcher row's Settings button; the shell owns Preferences, so it opens it.</summary>
    public event Action? SettingsRequested;

    /// <summary>Tree mutation requests (F2 / Delete / context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RenameRequested;
    public event Action<NotebookNode>? DeleteRequested;
    public event Action<NotebookNode?>? NewNoteRequested;
    public event Action<NotebookNode?>? NewSpreadsheetRequested;
    public event Action<NotebookNode?>? NewTaskboardRequested;
    public event Action<NotebookNode?>? NewFileRequested;
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>Raised when a row is dragged onto a folder: move it there, fulfilled by the shell.</summary>
    public event Action<NotebookNode, string>? MoveRequested;

    /// <summary>Hand-offs to the host OS (tree context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RevealRequested;
    public event Action<NotebookNode>? OpenExternallyRequested;

    /// <summary>
    /// The tree the user is looking at: the Filter panel's while they are in it, the Files panel's otherwise.
    /// Rename, Delete and the New menu act on a selection, and the selection they must mean is the one on
    /// screen — acting on a row hidden behind another panel is how a file nobody was looking at gets renamed.
    /// </summary>
    private IFileTreePanel ActiveTree => _active as IFileTreePanel ?? _files;

    /// <summary>The file tree's selected node, or null.</summary>
    public NotebookNode? SelectedTreeNode => ActiveTree.SelectedNode;

    /// <summary>Re-read the file tree from disk after a mutation.</summary>
    public void RefreshTree()
    {
        foreach (var tree in _trees)
        {
            tree.Refresh();
        }
    }

    /// <summary>Tell the file tree which document is open in the active tab, so it alone is drawn in bold.</summary>
    public void SetActiveDocument(string? path)
    {
        foreach (var tree in _trees)
        {
            tree.SetActiveDocument(path);
        }
    }

    /// <summary>
    /// Whether the file tree writes each file's extension after its name (the preference, and the session-only
    /// command that flips it). The shell owns the value; the sidebar only passes it down.
    /// </summary>
    public bool ShowFileExtensions
    {
        get => _files.ShowExtensions;
        set
        {
            foreach (var tree in _trees)
            {
                tree.ShowExtensions = value;
            }
        }
    }

    /// <summary>Reveal and select <paramref name="path"/> in the file tree (switching to the Files panel).</summary>
    public void RevealInTree(string path)
    {
        Activate("files");
        _files.SelectPath(path);
    }

    /// <summary>The id of the currently shown panel (test seam).</summary>
    internal string ActivePanelId => _active.Id;

    /// <summary>
    /// The panels the switcher row offers, in the order it draws them. The shell mirrors this into its command
    /// catalog so every button in the row is also a command (PRD §7.4) — read from here rather than listed
    /// again over there, because a row and a catalog that each keep their own list of the panels drift the
    /// moment one gains a panel the other has never heard of.
    /// </summary>
    internal IReadOnlyList<ISidebarPanel> Panels => _panels;

    /// <summary>The header's collapse control (test seam — click it as a user would).</summary>
    internal Button CollapseButtonForTest { get; }

    /// <summary>
    /// The header's expand/collapse-all-folders toggle — one button, two states (<see cref="BuildActions"/>).
    /// Also the test seam: click it as a user would.
    /// </summary>
    internal Button FoldersToggle { get; }

    /// <summary>The headers on the header New button's menu, in order (test seam).</summary>
    internal IReadOnlyList<string> NewMenuHeadersForTest => [.. _newItems.Select(i => i.Header as string ?? string.Empty)];

    /// <summary>Click an item on the New menu by its header (test seam).</summary>
    internal void InvokeNewMenuItemForTest(string header) =>
        _newItems
            .First(item => string.Equals(item.Header as string, header, StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    /// <summary>The outline panel's current items (test seam).</summary>
    internal IReadOnlyList<Panes.OutlineItem> OutlineItemsForTest => _outline.ItemsForTest;

    /// <summary>Rebuild for a newly opened notebook: header text + the file tree (or clear when null).</summary>
    public void SetRoot(NotebookNode? root)
    {
        _header.Text = root is { } r ? r.Name : "No notebook open";
        ToolTip.SetTip(_header, root?.Path); // the full path is one hover away, without shouting it in the header
        _actions.IsVisible = root is not null;
        foreach (var tree in _trees)
        {
            tree.SetRoot(root);
        }
    }

    /// <summary>Update the recent-files panel with <paramref name="paths"/> (most-recent first).</summary>
    public void SetRecent(string? notebookRoot, IReadOnlyList<string> paths) => _recent.SetItems(notebookRoot, paths);

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
        if (_panels.FirstOrDefault(p => p.Id == id) is not { } panel)
        {
            return;
        }

        if (ReferenceEquals(panel, _active))
        {
            // Already here. Pressing Search or Filter again is how you get back into the box after clicking
            // down into the results, so that one gesture still has to do something.
            FocusIfTypedInto(panel);
            return;
        }

        _active = panel;
        _body.Child = panel.View;
        RefreshSwitcher();

        // Both tree panels keep their own open folders, and one of them may not be a tree at all, so the
        // header's toggle has to re-read whichever tree the switch just put on screen.
        RefreshFoldersToggle();
        FocusIfTypedInto(panel);
        PanelActivated?.Invoke(panel.Id);
    }

    /// <summary>
    /// Put the keyboard in a panel that is typed into (<see cref="ISidebarPanel.FocusOnActivate"/>) — the box
    /// is what the user came for, so landing anywhere else costs a click that has nothing else to do.
    ///
    /// <para>After the view is mounted, because a control cannot take focus before it is in the tree.</para>
    /// </summary>
    private static void FocusIfTypedInto(ISidebarPanel panel)
    {
        if (panel.FocusOnActivate)
        {
            panel.Focus();
        }
    }

    /// <summary>
    /// The two things you reach for while looking at a file tree and could previously only get to through a menu
    /// or a chord: make something, and shut every folder you have opened rummaging around. They are icon buttons —
    /// tooltipped and named for a screen reader — so they cost the header no width it would rather give the
    /// notebook's name.
    ///
    /// <para>They carry no <c>Tag</c>: the panel switcher identifies its own buttons by a string tag, and these
    /// share a parent with it.</para>
    ///
    /// <para>The second is a <em>toggle</em>, and returns so the constructor can keep hold of it: shutting every
    /// folder leaves a button whose only remaining meaning is the opposite action, so it says so — chevrons-down,
    /// "Expand all folders" — rather than sitting there inert and identical, asking to be pressed again for
    /// nothing. See <see cref="RefreshFoldersToggle"/> for the two states.</para>
    /// </summary>
    private Button BuildActions(ThemePalette palette)
    {
        _actions.Children.Add(BuildNewButton(palette));

        var toggle = IconButton("chevrons-up", "Collapse all folders", palette, ToggleAllFolders);

        // The toggle rides inside a wrapper so that a *disabled* button can still explain itself on hover.
        // A tooltip belongs to the control it is set on, and a disabled control is exactly the one a pointer
        // may never be delivered to — which would make "why is this dim?" the one question the button
        // refuses to answer. The wrapper is always enabled, and takes the tip only while the button is not
        // (see RefreshFoldersToggle), so there is never a second tooltip competing with the button's own.
        // Transparent, not unpainted: a background-less panel is not hit-testable in Avalonia, and the
        // pointer would sail straight through it.
        _foldersToggleHost.Background = Brushes.Transparent;
        _foldersToggleHost.Child = toggle;
        _actions.Children.Add(_foldersToggleHost);
        return toggle;
    }

    /// <summary>
    /// Shut every folder, or — when they are all shut already — open every one. Which of the two it is, is
    /// exactly what the button was showing when it was pressed, so the click always does the thing its label and
    /// glyph just promised.
    /// </summary>
    private void ToggleAllFolders()
    {
        if (ActiveTree.AnyFolderExpanded)
        {
            ActiveTree.CollapseAll();
        }
        else
        {
            ActiveTree.ExpandAll();
        }
    }

    /// <summary>
    /// Point the toggle at whichever action is left. Driven by the tree's own re-render rather than by the click
    /// handler, so the button is equally right after a folder is opened from the twisty, the context menu, the
    /// keyboard, or a revealed document — not only after the header pressed it.
    ///
    /// <para>A notebook with no folders in it gets a disabled button: there is nothing to expand and nothing to
    /// collapse, and dimming it says so before the click rather than after. Dimming says <em>that</em> it is
    /// unavailable, though, never <em>why</em> — so hovering it now answers the question in words. Dim chrome
    /// is otherwise a dead end: nothing else on screen accounts for it, and the user is left to guess whether
    /// the button is broken, the notebook is empty, or they have missed a step.</para>
    /// </summary>
    private void RefreshFoldersToggle()
    {
        var collapsing = ActiveTree.AnyFolderExpanded;
        var label = collapsing ? "Collapse all folders" : "Expand all folders";

        if (FoldersToggle.Content is LucideIcon icon)
        {
            icon.IconKey = collapsing ? "chevrons-up" : "chevrons-down";
        }

        // The button is still called what it does — a screen reader should hear the action, not the
        // obstacle — so the reason rides as its help text, the way the switcher row speaks "selected".
        AutomationProperties.SetName(FoldersToggle, label);

        FoldersToggle.IsEnabled = ActiveTree.HasExpandableFolder;
        FoldersToggle.Opacity = FoldersToggle.IsEnabled ? 1 : 0.4;

        // Phrased from the offer the button is making, so the two never disagree. In practice that word is
        // always "expanding" today — a tree with a folder open always has one to collapse, so it is never
        // both offering Collapse and disabled — but the sentence is built from the same flag as the label
        // rather than from that reasoning, which stops being true the moment either rule moves.
        var reason = $"No folders available for {(collapsing ? "collapsing" : "expanding")}";

        // Exactly one of the two carries the tip, so only one message can ever appear (see BuildActions).
        ToolTip.SetTip(FoldersToggle, FoldersToggle.IsEnabled ? label : null);
        ToolTip.SetTip(_foldersToggleHost, FoldersToggle.IsEnabled ? null : reason);
        AutomationProperties.SetHelpText(FoldersToggle, FoldersToggle.IsEnabled ? null : reason);
    }

    /// <summary>
    /// The header's "New" control: one plus button that drops the five things a notebook is made of. It used to
    /// be a New Note button, which made the one type it wrote the only one you could reach without going through
    /// a menu or knowing a chord — a board or a spreadsheet cost the same trip either way. A menu says what the
    /// notebook can hold in one glance and keeps the header at one glyph wide.
    ///
    /// <para>Folder leads, then the three document types the shell knows how to seed, then the escape hatch for
    /// everything else. The target folder is read at <em>click</em> time, from whichever tree is on screen
    /// (<see cref="ActiveTree"/>), so the menu acts on the selection you can see, exactly as the tree's own
    /// context menu does.</para>
    /// </summary>
    private Button BuildNewButton(ThemePalette palette)
    {
        _newItems =
        [
            MenuEntry("New Folder…", () => NewFolderRequested?.Invoke(ActiveTree.SelectedNode)),
            MenuEntry("New Note…", () => NewNoteRequested?.Invoke(ActiveTree.SelectedNode)),
            MenuEntry("New Spreadsheet…", () => NewSpreadsheetRequested?.Invoke(ActiveTree.SelectedNode)),
            MenuEntry("New Taskboard…", () => NewTaskboardRequested?.Invoke(ActiveTree.SelectedNode)),
            MenuEntry("New File…", () => NewFileRequested?.Invoke(ActiveTree.SelectedNode)),
        ];

        var button = IconButtonChrome("plus", "New", palette);

        // Built once and handed over as ItemsSource at construction: the items never vary (the folder they act
        // on is resolved in the handler), and a source assigned later — inside Opening — is not picked up.
        button.Flyout = new MenuFlyout { ItemsSource = _newItems, Placement = PlacementMode.BottomEdgeAlignedRight };
        return button;
    }

    private static MenuItem MenuEntry(string header, Action run)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => run();
        return item;
    }

    /// <summary>
    /// The sidebar chrome's icon-button shape — transparent, 28×28 (a real pointer target, WCAG 2.2 §2.5.8),
    /// tooltipped and named for a screen reader. Internal so the collapsed-state handle in the shell is built
    /// from the same one and the control doesn't change shape when it changes place.
    /// </summary>
    internal static Button IconButton(string icon, string label, ThemePalette palette, Action run)
    {
        var button = IconButtonChrome(icon, label, palette);
        button.Click += (_, _) => run();
        return button;
    }

    /// <summary>The shape alone, for a button whose click opens a menu rather than running something.</summary>
    private static Button IconButtonChrome(string icon, string label, ThemePalette palette)
    {
        var button = new Button
        {
            Content = new LucideIcon { IconKey = icon, Size = 15, Foreground = palette.MutedText },
            Width = 28,
            Height = 28, // a real pointer target (WCAG 2.2 §2.5.8)
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>
    /// The switcher row: one icon per panel, then Settings.
    ///
    /// <para>The four panels are named by glyph rather than by word. Four words in a sidebar this narrow spend
    /// most of the row on labels for panels a user learns once and thereafter recognises by shape — and the
    /// row had no space left to grow into. The word doesn't disappear, it moves: every button is tooltipped
    /// and carries the panel's title as its accessible name, so a screen reader still hears "Files", and the
    /// active panel is still called out (<see cref="RefreshSwitcher"/>).</para>
    ///
    /// <para>Settings sits at the end of the row, after a gap that says it is not a fifth panel — it opens the
    /// Preferences dialog, so it never lights up as "the panel you are in". It carries no <c>Tag</c>, which is
    /// how the switcher tells its own buttons from a neighbour's.</para>
    /// </summary>
    private void BuildSwitcher()
    {
        foreach (var panel in _panels)
        {
            var button = SwitcherButton(panel.Icon, panel.Title, () => Activate(panel.Id));
            button.Tag = panel.Id;
            _switcher.Children.Add(button);
        }

        var settings = SwitcherButton("settings-2", "Settings", () => SettingsRequested?.Invoke());
        settings.Margin = new Thickness(8, 0, 0, 0); // the gap that separates an action from the panel switcher
        _switcher.Children.Add(settings);

        RefreshSwitcher();
    }

    /// <summary>
    /// One button in the switcher row: an icon-only 28×28 target (WCAG 2.2 §2.5.8) that reserves two pixels
    /// at its foot for the active-panel underline — reserved on every button, active or not, so switching
    /// panels doesn't shift the row.
    /// </summary>
    private static Button SwitcherButton(string icon, string label, Action run)
    {
        var button = new Button
        {
            Content = new LucideIcon { IconKey = icon, Size = 16 },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(0),
            Width = 28,
            MinHeight = 28,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        button.Click += (_, _) => run();
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>
    /// Light the active nav item — with an underline, not just a colour.
    ///
    /// <para>Colour was previously the <i>only</i> thing distinguishing the active panel from the other three,
    /// which leaves a user with a colour-vision deficiency no signal at all (WCAG 2.2 §1.4.1, Use of Color). The
    /// active document tab already carries a non-colour indicator; this mirrors it, so the two "you are here"
    /// signals in the shell are spelled the same way. It is the whole non-colour signal now that the row is
    /// icons: a glyph has no weight to thicken the way the old text labels did.</para>
    ///
    /// <para>The Settings button is in this row but carries no id, so it lands in the inactive branch and stays
    /// there — correctly, since it is an action rather than a panel you can be inside of.</para>
    /// </summary>
    private void RefreshSwitcher()
    {
        var palette = Tokens.Active;
        foreach (var button in _switcher.Children.OfType<Button>())
        {
            bool active = (string?)button.Tag == _active.Id;
            var brush = active ? palette.Accent : palette.MutedText;
            button.Foreground = brush;
            button.BorderBrush = active ? palette.Accent : Brushes.Transparent;
            AutomationProperties.SetHelpText(button, active ? "selected" : null);

            // Named on the glyph as well as on the button: the icon is a bare Control, not text, so it takes
            // the accent only if it is handed it.
            if (button.Content is LucideIcon glyph)
            {
                glyph.Foreground = brush;
            }
        }
    }

    private static T Dock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }
}
