using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
    private readonly FilesPanel _files = new();
    private readonly SearchPanel _search = new();
    private readonly OutlinePanel _outline = new();
    private readonly RecentPanel _recent = new();
    private readonly List<ISidebarPanel> _panels;

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
        _files.RevealRequested += node => RevealRequested?.Invoke(node);
        _files.OpenExternallyRequested += node => OpenExternallyRequested?.Invoke(node);
        _files.PinnedChanged += () => PinnedChanged?.Invoke();

        var palette = Tokens.Dark;
        _header.Foreground = palette.MutedText;

        BuildSwitcher();
        BuildActions(palette);
        _body.Child = _active.View;

        // The collapse control leads the row, in the window's top-left corner — the same spot the handle takes
        // over once the sidebar is hidden, so the affordance never moves. It sits outside `_actions` because it
        // is meaningful with no notebook open (the other two aren't), and so it never hides itself away.
        var collapse = IconButton("panel-left", "Hide sidebar", palette, () => ToggleRequested?.Invoke());
        collapse.Margin = new Thickness(6, 0, 0, 0);
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

    /// <summary>Raised when the file tree's pinned folders change, so the shell persists them to the session.</summary>
    public event Action? PinnedChanged;

    /// <summary>Tree mutation requests (F2 / Delete / context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RenameRequested;
    public event Action<NotebookNode>? DeleteRequested;
    public event Action<NotebookNode?>? NewNoteRequested;
    public event Action<NotebookNode?>? NewSpreadsheetRequested;
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>Hand-offs to the host OS (tree context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RevealRequested;
    public event Action<NotebookNode>? OpenExternallyRequested;

    /// <summary>The file tree's selected node, or null.</summary>
    public NotebookNode? SelectedTreeNode => _files.SelectedNode;

    /// <summary>Re-read the file tree from disk after a mutation.</summary>
    public void RefreshTree() => _files.Refresh();

    /// <summary>Tell the file tree which document is open in the active tab, so it alone is drawn in bold.</summary>
    public void SetActiveDocument(string? path) => _files.SetActiveDocument(path);

    /// <summary>Reveal and select <paramref name="path"/> in the file tree (switching to the Files panel).</summary>
    public void RevealInTree(string path)
    {
        Activate("files");
        _files.SelectPath(path);
    }

    /// <summary>The id of the currently shown panel (test seam).</summary>
    internal string ActivePanelId => _active.Id;

    /// <summary>The header's collapse control (test seam — click it as a user would).</summary>
    internal Button CollapseButtonForTest { get; }

    /// <summary>The outline panel's current items (test seam).</summary>
    internal IReadOnlyList<Panes.OutlineItem> OutlineItemsForTest => _outline.ItemsForTest;

    /// <summary>Rebuild for a newly opened notebook: header text + the file tree (or clear when null).</summary>
    public void SetRoot(NotebookNode? root)
    {
        _header.Text = root is { } r ? r.Name : "No notebook open";
        ToolTip.SetTip(_header, root?.Path); // the full path is one hover away, without shouting it in the header
        _actions.IsVisible = root is not null;
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

    /// <summary>
    /// The two things you reach for while looking at a file tree and could previously only get to through a menu
    /// or a chord: make a note, and shut every folder you have opened rummaging around. They are icon buttons —
    /// tooltipped and named for a screen reader — so they cost the header no width it would rather give the
    /// notebook's name.
    ///
    /// <para>They carry no <c>Tag</c>: the panel switcher identifies its own buttons by a string tag, and these
    /// share a parent with it.</para>
    /// </summary>
    private void BuildActions(ThemePalette palette)
    {
        _actions.Children.Add(IconButton(
            "file-plus", "New note", palette, () => NewNoteRequested?.Invoke(_files.SelectedNode)));
        _actions.Children.Add(IconButton(
            "chevrons-up", "Collapse all folders", palette, () => _files.CollapseAll()));
    }

    /// <summary>
    /// The sidebar chrome's icon-button shape — transparent, 28×28 (a real pointer target, WCAG 2.2 §2.5.8),
    /// tooltipped and named for a screen reader. Internal so the collapsed-state handle in the shell is built
    /// from the same one and the control doesn't change shape when it changes place.
    /// </summary>
    internal static Button IconButton(string icon, string label, ThemePalette palette, Action run)
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
        button.Click += (_, _) => run();
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
        var palette = Tokens.Dark;
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
