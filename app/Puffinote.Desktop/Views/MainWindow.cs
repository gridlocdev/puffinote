using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Commands;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;

namespace Puffinote.Desktop.Views;

/// <summary>
/// Puffinote's single window (PRD §5 — one notebook, one window): a menu (macOS native bar, or an in-window
/// bar elsewhere), a collapsible keyboard-navigable sidebar with the file tree, and a <see cref="TabHost"/>
/// editor area that hosts N open documents (notes and sheets), with a shared <see cref="OverlayHost"/> for
/// the dirty-close prompt and messages. All chrome reads the shared <see cref="Tokens"/> so it matches the
/// family.
/// </summary>
public class MainWindow : Window
{
    private static readonly KeyModifiers PrimaryModifier =
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private readonly NotebookService _notebook;

    // Puffinote's per-user override settings (PRD §7.5), loaded once at startup. Resolved against each
    // notebook's own .puffinote/settings.json on open and handed to the tab host, which layers them over an
    // editor's own config when a note/sheet pane opens.
    private readonly PuffinoteSettings _settings = PuffinoteSettings.LoadUser();

    // The user's custom shell shortcuts (PRD §7.6), edited in the shortcuts editor. BuildShellCommands
    // resolves each non-reserved command's gesture through this, so a rebound key reaches menu + palette +
    // key bindings alike. Reserved global keys are fixed and never routed through it.
    private ShortcutKeymap _keymap = ShortcutKeymap.Load();

    private readonly SidebarView _sidebarView = new();
    private ColumnDefinition _sidebarColumn = new(260, GridUnitType.Pixel) { MinWidth = 160 };
    private Border _sidebar = null!;
    private GridSplitter _splitter = null!;
    private GridLength _lastSidebarWidth = new(260, GridUnitType.Pixel);

    // The notebook-scoped recent-files list (PRD §7.2.3), maintained through the shared RecentFiles rule and
    // shown in the sidebar's Recent panel plus seeded into quick-open. Persisted per notebook to
    // .puffinote/session.json (PRD §7.1) via SessionStore — loaded on open, saved as it changes.
    private List<string> _recent = new();

    // The notebook's background full-text search index (PRD §7.2.3), recreated per notebook. Null until a notebook
    // is open. Rebuilt off the UI thread on open and after tree mutations.
    private Search.NotebookSearchService? _search;

    // Watches the notebook for external filesystem changes to refresh the tree + index live (PRD §7.1).
    private Shell.NotebookWatcher? _watcher;

    private readonly TabHost _tabHost = new();
    private readonly OverlayHost _overlayHost = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
    };

    // Puffinote's one shell command catalog (PRD §7.4): menu, key bindings, and the palette all derive
    // from it. _relays holds one shared RelayCommand per entry so their enabled state stays in step.
    private List<AppCommand> _shellCommands = new();
    private Dictionary<string, AppCommand> _shellById = new();
    private Dictionary<string, RelayCommand> _relays = new();
    private bool _closingConfirmed;

    // The moderate reserved global set (PRD §7.6): these shell keys win over the focused editor via a
    // tunnelling handler, so e.g. ⌘K always opens Puffinote's palette, not the sheet's. focusTree has no
    // gesture, so it isn't routed. Everything else is focus-dispatched (ordinary bubbling key bindings).
    private static readonly HashSet<string> ReservedIds = new()
    {
        "shell.commandPalette", "shell.quickOpen", "shell.openNotebook", "shell.toggleSidebar",
        "shell.closeTab", "shell.nextTab", "shell.prevTab",
    };
    private List<(KeyGesture Gesture, RelayCommand Command)> _reservedBindings = new();
    private bool _conflictWarned;
    private IEditorPane? _lastScannedPane;

    // The menu bar is rebuilt when a shortcut is rebound so its gesture labels stay fresh; keep the roots to
    // swap it (in-window) or re-set it (macOS native).
    private DockPanel _menuRoot = null!;
    private Menu? _inWindowMenu;
    private ShortcutsEditorView? _shortcutsView;

    public MainWindow(NotebookService notebook)
    {
        _notebook = notebook;
        _notebook.NotebookOpened += (_, _) => OnNotebookOpened();
        _sidebarView.FileActivated += path => _ = OpenFileAsync(path);
        _sidebarView.SelectionChanged += _ => RefreshSelectionCommands();
        _sidebarView.PanelActivated += id => { if (id == "outline") RefreshOutline(); };
        _sidebarView.NewFileRequested += node => _ = PromptNewFileAsync(node);
        _sidebarView.NewFolderRequested += node => _ = PromptNewFolderAsync(node);
        _sidebarView.RenameRequested += node => _ = PromptRenameAsync(node);
        _sidebarView.DeleteRequested += node => _ = PromptDeleteAsync(node);

        Width = 1000;
        Height = 700;
        Title = "Puffinote";

        var palette = Tokens.Dark;

        _tabHost.ConfirmClose = PromptCloseAsync;
        _tabHost.Changed += (_, _) => UpdatePaneChrome();

        var root = new DockPanel();
        InstallMenu(root);
        root.Children.Add(BuildBody(palette));

        // Stack the shared overlay host above the content so dialogs (dirty-close prompt, messages) render
        // over the whole window with a dimmed backdrop.
        Content = new Panel { Children = { root, _overlayHost } };

        Closing += OnWindowClosing;
        Closed += (_, _) => { PersistSession(); _watcher?.Dispose(); };
    }

    /// <summary>Open <paramref name="path"/> as the notebook (used by the CLI-arg path and the menu).</summary>
    public void OpenNotebook(string path) => _notebook.OpenNotebook(path);

    /// <summary>The notebook's recent-files list, most-recent first (test seam).</summary>
    internal IReadOnlyList<string> RecentForTest => _recent;

    /// <summary>Whether a modal/overlay is currently shown (test seam).</summary>
    internal bool IsOverlayOpenForTest => _overlayHost.IsOpen;

    /// <summary>The id of the sidebar's active panel (test seam).</summary>
    internal string ActiveSidebarPanelForTest => _sidebarView.ActivePanelId;

    /// <summary>The sidebar (test seam — drive the tree in end-to-end mutation tests).</summary>
    internal Sidebar.SidebarView SidebarForTest => _sidebarView;

    /// <summary>The notebook search service (test seam — await a deterministic rebuild, then query).</summary>
    internal Search.NotebookSearchService? SearchForTest => _search;

    /// <summary>The active pane's editor control (test seam — assert config inheritance reached the editor).</summary>
    internal Control? ActiveEditorViewForTest => _tabHost.ActivePane?.View;

    /// <summary>Redirect the shell keymap to a test file (so rebinding tests don't touch the real user config).</summary>
    internal void UseKeymapForTest(ShortcutKeymap keymap)
    {
        _keymap = keymap;
        BuildShellCommands();
        ApplyShellShortcuts();
        RefreshMenus();
    }

    /// <summary>Drive a rebinding as the shortcuts editor would (test seam).</summary>
    internal void RebindShellForTest(string id, Puffinote.Components.Commands.Shortcut? shortcut) => OnShortcutRebound(id, shortcut);

    /// <summary>The effective shortcut a shell command currently resolves to (test seam).</summary>
    internal Puffinote.Components.Commands.Shortcut? ShellShortcutForTest(string id) => _shellById[id].Shortcut;

    /// <summary>Run a shell command by id, as the menu/palette would (test seam).</summary>
    internal void InvokeShellForTest(string id) => _shellById[id].Execute();

    private Control BuildBody(ThemePalette palette)
    {
        _sidebar = new Border
        {
            Background = palette.Surface,
            Child = _sidebarView,
        };

        _splitter = new GridSplitter { Width = 4, Background = palette.SurfaceBorder };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                _sidebarColumn,
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        Grid.SetColumn(_sidebar, 0);
        Grid.SetColumn(_splitter, 1);
        Grid.SetColumn(_tabHost, 2);
        grid.Children.Add(_sidebar);
        grid.Children.Add(_splitter);
        grid.Children.Add(_tabHost);
        return grid;
    }

    // ---- Command catalog → menu (macOS native / in-window), key bindings, and palette --------------

    /// <summary>Puffinote's shell catalog — defined once; the menu, key bindings, and palette all read it.</summary>
    private void BuildShellCommands()
    {
        // A command's effective gesture: the user's custom binding (shortcuts editor) if it has one, else the
        // default below. Reserved global keys are fixed — they're never routed through the keymap (PRD §7.6).
        Shortcut? Bound(string id, Shortcut? def) => ReservedIds.Contains(id) ? def : _keymap.Resolve(id, def);

        _shellCommands = new List<AppCommand>
        {
            new("shell.commandPalette", "Command Palette", "Search and run any command", Bound("shell.commandPalette", Shortcut.Cmd(Key.K)),
                ShowCommandPalette, icon: "command"),
            new("shell.quickOpen", "Quick Open…", "Jump to any file in the notebook", Bound("shell.quickOpen", Shortcut.Cmd(Key.O)),
                ShowQuickOpen, () => _notebook.Root is not null, icon: "search"),
            new("shell.openNotebook", "Open Notebook…", "Open a folder as the notebook", Bound("shell.openNotebook", Shortcut.Cmd(Key.O, KeyModifiers.Shift)),
                () => _ = OpenNotebookInteractiveAsync(), icon: "folder-open"),
            new("shell.newFile", "New File…", "Create a new file in the selected folder", Bound("shell.newFile", null),
                () => _ = PromptNewFileAsync(_sidebarView.SelectedTreeNode), () => _notebook.Root is not null, icon: "file-plus"),
            new("shell.newFolder", "New Folder…", "Create a new folder in the selected folder", Bound("shell.newFolder", null),
                () => _ = PromptNewFolderAsync(_sidebarView.SelectedTreeNode), () => _notebook.Root is not null),
            new("shell.renameEntry", "Rename…", "Rename the selected file or folder", Bound("shell.renameEntry", null),
                () => { if (_sidebarView.SelectedTreeNode is { } n) _ = PromptRenameAsync(n); }, CanMutateSelected, icon: "pencil"),
            new("shell.deleteEntry", "Delete", "Delete the selected file or folder", Bound("shell.deleteEntry", null),
                () => { if (_sidebarView.SelectedTreeNode is { } n) _ = PromptDeleteAsync(n); }, CanMutateSelected, icon: "trash-2"),
            new("shell.save", "Save", "Save the active document", Bound("shell.save", Shortcut.Cmd(Key.S)),
                () => _ = SaveCurrentAsync(), () => _tabHost.ActivePane is not null, icon: "save"),
            new("shell.closeTab", "Close Tab", "Close the active tab", Bound("shell.closeTab", Shortcut.Cmd(Key.W)),
                () => _ = _tabHost.CloseActiveAsync(), () => _tabHost.ActivePane is not null),
            new("shell.nextTab", "Next Tab", "Switch to the next tab", Bound("shell.nextTab", Shortcut.Cmd(Key.OemCloseBrackets, KeyModifiers.Shift)),
                () => _tabHost.ActivateRelative(1)),
            new("shell.prevTab", "Previous Tab", "Switch to the previous tab", Bound("shell.prevTab", Shortcut.Cmd(Key.OemOpenBrackets, KeyModifiers.Shift)),
                () => _tabHost.ActivateRelative(-1)),
            new("shell.toggleSidebar", "Toggle Sidebar", "Show or hide the sidebar", Bound("shell.toggleSidebar", Shortcut.Cmd(Key.OemBackslash)),
                ToggleSidebar),
            new("shell.focusTree", "Focus File Tree", "Move keyboard focus to the file tree", Bound("shell.focusTree", null),
                () => _sidebarView.FocusTree()),
            new("shell.shortcuts", "Keyboard Shortcuts…", "View and customize keyboard shortcuts", Bound("shell.shortcuts", null),
                ShowShortcutsEditor),
            new("shell.about", "About Puffinote", "About this app", null, ShowAbout),
        };
        _shellById = _shellCommands.ToDictionary(c => c.Id);
    }

    private void InstallMenu(DockPanel root)
    {
        _menuRoot = root;
        BuildShellCommands();

        // One shared RelayCommand per catalog entry, resolved by id so it survives a catalog rebuild when a
        // shortcut is rebound (the menu items and key bindings keep pointing at a live command). Enabled
        // state is refreshed for all of them together by RefreshCommandStates.
        _relays = _shellCommands.ToDictionary(
            c => c.Id,
            c => new RelayCommand(() => _shellById[c.Id].Execute(), () => _shellById[c.Id].IsEnabled));

        ApplyShellShortcuts();
        AddHandler(KeyDownEvent, OnReservedKeyDown, RoutingStrategies.Tunnel);
        RefreshMenus();
    }

    /// <summary>
    /// (Re)apply the shell's key bindings from the current catalog: non-reserved commands become ordinary
    /// (bubbling) key bindings — the focused editor gets first refusal — while reserved ones are collected
    /// for the tunnel router so they always win (PRD §7.6). Called at startup and after a rebinding, so a new
    /// key takes effect live.
    /// </summary>
    private void ApplyShellShortcuts()
    {
        KeyBindings.Clear();
        foreach (var command in _shellCommands)
        {
            if (command.Gesture is { } gesture && !ReservedIds.Contains(command.Id))
            {
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = _relays[command.Id] });
            }
        }

        _reservedBindings = _shellCommands
            .Where(c => ReservedIds.Contains(c.Id) && c.Gesture is not null)
            .Select(c => (c.Gesture!, _relays[c.Id]))
            .ToList();
    }

    /// <summary>(Re)build the menu bar from the current catalog so rebound gestures show their new keys.</summary>
    private void RefreshMenus()
    {
        if (OperatingSystem.IsMacOS())
        {
            NativeMenu.SetMenu(this, new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem { Header = "File", Menu = BuildNativeMenu("shell.openNotebook", "shell.quickOpen", "shell.newFile", "shell.newFolder", "shell.save", "shell.closeTab") },
                    new NativeMenuItem { Header = "View", Menu = BuildNativeMenu("shell.commandPalette", "shell.toggleSidebar", "shell.nextTab", "shell.prevTab") },
                    new NativeMenuItem { Header = "Help", Menu = BuildNativeMenu("shell.shortcuts", "shell.about") },
                },
            });
            return;
        }

        if (_inWindowMenu is { } existing)
        {
            _menuRoot.Children.Remove(existing);
        }

        _inWindowMenu = new Menu
        {
            Items =
            {
                new MenuItem { Header = "_File", ItemsSource = BuildMenuItems("shell.openNotebook", "shell.quickOpen", "shell.newFile", "shell.newFolder", "shell.save", "shell.closeTab") },
                new MenuItem { Header = "_View", ItemsSource = BuildMenuItems("shell.commandPalette", "shell.toggleSidebar", "shell.nextTab", "shell.prevTab") },
                new MenuItem { Header = "_Help", ItemsSource = BuildMenuItems("shell.shortcuts", "shell.about") },
            },
        };
        DockPanel.SetDock(_inWindowMenu, Avalonia.Controls.Dock.Top);
        _menuRoot.Children.Insert(0, _inWindowMenu);
    }

    private List<MenuItem> BuildMenuItems(params string[] ids) =>
        ids.Select(id => _shellById[id]).Select(c =>
            new MenuItem { Header = c.Title, Command = _relays[c.Id], InputGesture = c.Gesture }).ToList();

    private NativeMenu BuildNativeMenu(params string[] ids)
    {
        var menu = new NativeMenu();
        foreach (var id in ids)
        {
            var c = _shellById[id];
            menu.Items.Add(new NativeMenuItem { Header = c.Title, Command = _relays[c.Id], Gesture = c.Gesture });
        }

        return menu;
    }

    /// <summary>Open the merged Command Palette (⌘K): Puffinote's shell commands + the active editor's catalog.</summary>
    private void ShowCommandPalette()
    {
        if (_overlayHost.IsOpen)
        {
            return; // an overlay is already up (e.g. a dialog opened from the native menu) — don't clobber it
        }

        var editor = _tabHost.ActivePane?.Commands ?? (IReadOnlyList<AppCommand>)Array.Empty<AppCommand>();
        var entries = CommandMerge.ToEntries(_shellCommands, editor);

        var palette = new PaletteListView { Placeholder = "Type a command…" };
        palette.SetItems(entries);
        palette.Chosen += entry => { _overlayHost.Close(); entry.Run(); };
        palette.Cancelled += (_, _) => _overlayHost.Close();
        ShowPaletteOverlay(palette);
    }

    /// <summary>
    /// Quick-open (⌘O, PRD §7.2.5): a fuzzy file switcher, separate from the command palette. It lists every
    /// notebook file — most-recent first for an empty query — and opens the chosen file into a tab. Reuses the
    /// shared <see cref="PaletteListView"/> (which fuzzy-ranks by name as you type).
    /// </summary>
    private void ShowQuickOpen()
    {
        if (_overlayHost.IsOpen || _notebook.Root is null)
        {
            return;
        }

        var ordered = QuickOpen.OrderByRecency(_notebook.EnumerateFiles(), _recent);
        var entries = ordered.Select(path => new PaletteEntry(
            Title: System.IO.Path.GetFileName(path),
            Run: () => _ = OpenFileAsync(path),
            Description: RelativeToNotebook(path))).ToList();

        var palette = new PaletteListView { Placeholder = "Go to file…" };
        palette.SetItems(entries);
        palette.Chosen += entry => { _overlayHost.Close(); entry.Run(); };
        palette.Cancelled += (_, _) => _overlayHost.Close();
        ShowPaletteOverlay(palette);
    }

    /// <summary>Present <paramref name="palette"/> in the shared overlay card and focus its search box.</summary>
    private void ShowPaletteOverlay(PaletteListView palette)
    {
        var card = new Border
        {
            Width = 560,
            Background = Tokens.Dark.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 80, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = palette,
        };
        _overlayHost.ShowContent(card);
        Dispatcher.UIThread.Post(() => palette.SearchBox.Focus());
    }

    /// <summary>A notebook file's path relative to the notebook root (for the quick-open / recent secondary line).</summary>
    private string RelativeToNotebook(string path) => NotebookPaths.RelativeTo(_notebook.RootPath, path);

    /// <summary>
    /// Reserved global shortcuts win over the focused editor (PRD §7.6): on the tunnelling route — before
    /// the editor's own key handling — a reserved gesture runs its shell command and stops the event. While
    /// an overlay is open, keys belong to the overlay, so the router stands down.
    /// </summary>
    private void OnReservedKeyDown(object? sender, KeyEventArgs e)
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        if (ShortcutRouter.Match(_reservedBindings, e.Key, e.KeyModifiers) is { } command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true; // reserved: the editor never sees it
        }
    }

    /// <summary>
    /// When the active document's editor first appears, warn once if any of its shortcuts collide with a
    /// reserved global key — the reserved key wins, so the editor binding is shadowed (PRD §7.6).
    /// </summary>
    private void ScanForShortcutConflicts()
    {
        // Don't scan or warn while an overlay owns the screen — retry on the next pane change, so the
        // once-guard is never burned without the warning actually appearing (C4).
        if (_conflictWarned || _overlayHost.IsOpen)
        {
            return;
        }

        if (_tabHost.ActivePane is not { } pane || ReferenceEquals(pane, _lastScannedPane))
        {
            return;
        }

        _lastScannedPane = pane;
        var reserved = _shellCommands.Where(c => ReservedIds.Contains(c.Id));
        var conflicts = ShortcutConflictScanner.Find(reserved, pane.Commands);
        if (conflicts.Count == 0)
        {
            return;
        }

        _conflictWarned = true; // latch only now that we're actually presenting the warning
        _ = WarnAboutShortcutConflictsAsync(conflicts);
    }

    /// <summary>
    /// The friendly, actionable conflict notice (PRD §7.6): name the shadowed editor shortcuts, say the
    /// reserved shell key wins, and offer a one-click path to the shortcuts editor to rebind (or keep as-is).
    /// </summary>
    private async Task WarnAboutShortcutConflictsAsync(IReadOnlyList<ShortcutConflict> conflicts)
    {
        var lines = conflicts
            .Select(c => $"•  {c.Editor.ShortcutText}  {c.Editor.Title}  →  “{c.Reserved.Title}” (Puffinote)")
            .ToList();
        var openEditor = await _overlayHost.ShowModalAsync(new ConfirmDialog(
            "Some editor shortcuts are overridden",
            "This editor defines shortcuts that Puffinote reserves for the shell, so Puffinote's shortcut "
                + "wins and the editor's is shadowed here:\n\n" + string.Join("\n", lines),
            confirmLabel: "Open Shortcuts Editor",
            denyLabel: "Keep as-is",
            confirmIsDefault: false));

        if (openEditor == true)
        {
            ShowShortcutsEditor();
        }
    }

    private async Task OpenNotebookInteractiveAsync()
    {
        var top = GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Notebook",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            OpenNotebook(path);
        }
    }

    private void ToggleSidebar()
    {
        var collapsing = _sidebarColumn.Width.Value > 0;
        if (collapsing)
        {
            _lastSidebarWidth = _sidebarColumn.Width;
            _sidebarColumn.Width = new GridLength(0);
            _sidebarColumn.MinWidth = 0;
            _sidebar.IsVisible = false;
            _splitter.IsVisible = false;
        }
        else
        {
            _sidebarColumn.MinWidth = 160;
            _sidebarColumn.Width = _lastSidebarWidth;
            _sidebar.IsVisible = true;
            _splitter.IsVisible = true;
        }

        PersistSession(); // remember the collapsed/expanded state per notebook (PRD §7.1)
    }

    /// <summary>
    /// Open the shared shortcuts editor (PRD §7.6): every shell binding plus the active editor's, grouped and
    /// with the reserved global keys marked. Rebinding a shell command persists to the keymap and takes
    /// effect immediately; editor bindings are listed for reference (they're the editor's own to change).
    /// </summary>
    private void ShowShortcutsEditor()
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        var view = new ShortcutsEditorView();
        view.Rebound += OnShortcutRebound;
        view.SetBindings(BuildShortcutRows());
        _shortcutsView = view;

        var card = new Border
        {
            Width = 560,
            Background = Tokens.Dark.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 60, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Keyboard Shortcuts", FontWeight = FontWeight.SemiBold, FontSize = 15 },
                    view,
                },
            },
        };

        _overlayHost.Closed += ForgetShortcutsView;
        _overlayHost.ShowContent(card);
        Dispatcher.UIThread.Post(() => view.Focus());
    }

    private void ForgetShortcutsView(object? sender, EventArgs e)
    {
        _shortcutsView = null;
        _overlayHost.Closed -= ForgetShortcutsView;
    }

    /// <summary>The rows the shortcuts editor shows: Puffinote's shell catalog, then the active editor's.</summary>
    private List<ShortcutBinding> BuildShortcutRows()
    {
        var rows = _shellCommands.Select(c => new ShortcutBinding(
            c.Id, c.Title, "Puffinote", c.Shortcut,
            IsReserved: ReservedIds.Contains(c.Id),
            IsRebindable: !ReservedIds.Contains(c.Id),
            IsCustomized: _keymap.For(c.Id) is not null)).ToList();

        if (_tabHost.ActivePane is { } pane)
        {
            rows.AddRange(pane.Commands.Select(c => new ShortcutBinding(
                c.Id, c.Title, "Editor", c.Shortcut, IsReserved: false, IsRebindable: false, IsCustomized: false)));
        }

        return rows;
    }

    /// <summary>Apply a rebinding from the shortcuts editor: persist it and re-derive menu/keys/palette live.</summary>
    private void OnShortcutRebound(string id, Shortcut? shortcut)
    {
        if (ReservedIds.Contains(id))
        {
            return; // reserved global keys are fixed (PRD §7.6) — never rebound from within Puffinote
        }

        _keymap.Set(id, shortcut);
        BuildShellCommands();   // re-resolve every command's gesture through the updated keymap
        ApplyShellShortcuts();  // the new key binding takes effect immediately
        RefreshMenus();         // and the menu labels stay truthful
        RefreshCommandStates();
        _shortcutsView?.SetBindings(BuildShortcutRows()); // refresh the open editor's rows
    }

    private void ShowAbout()
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        _ = _overlayHost.ShowModalAsync(new MessageDialog(
            "Puffinote",
            "A second-brain notes app — composes Typage (Markdown) and Cellestra (spreadsheets)."));
    }

    private void OnNotebookOpened()
    {
        _sidebarView.SetRoot(_notebook.Root);

        // Resolve Puffinote's overrides for this notebook (per-user + the notebook's own .puffinote/settings.json)
        // before any file opens, so panes inherit the editor's config with these layered on top (PRD §7.5).
        _tabHost.Settings = PuffinoteSettings.Resolve(_settings, _notebook.RootPath);

        // Restore the per-notebook session (PRD §7.1): recent files + sidebar layout. Recency is per-notebook, so
        // a switch loads the new notebook's list (empty when it has none); drop recent entries whose file is
        // now gone so a stale path never shows.
        var session = _notebook.RootPath is { } root ? SessionStore.Load(root) : new NotebookSession();
        _recent = session.Recent.Where(File.Exists).ToList();
        _sidebarView.SetRecent(_notebook.RootPath, _recent);
        ApplySidebarState(session.SidebarCollapsed, session.SidebarWidth);

        StartSearchIndex();
        StartWatching();
        UpdatePaneChrome();
    }

    /// <summary>Restore the sidebar's persisted collapsed state and width (PRD §7.1).</summary>
    private void ApplySidebarState(bool collapsed, double width)
    {
        _lastSidebarWidth = new GridLength(width > 0 ? width : SessionStore.DefaultSidebarWidth, GridUnitType.Pixel);
        if (collapsed)
        {
            _sidebarColumn.Width = new GridLength(0);
            _sidebarColumn.MinWidth = 0;
            _sidebar.IsVisible = false;
            _splitter.IsVisible = false;
        }
        else
        {
            _sidebarColumn.MinWidth = 160;
            _sidebarColumn.Width = _lastSidebarWidth;
            _sidebar.IsVisible = true;
            _splitter.IsVisible = true;
        }
    }

    /// <summary>Write the current recent list + sidebar layout to the notebook's session file (best-effort).</summary>
    private void PersistSession()
    {
        if (_notebook.RootPath is not { } root)
        {
            return;
        }

        var collapsed = !_sidebar.IsVisible;
        var width = (collapsed ? _lastSidebarWidth : _sidebarColumn.Width).Value;
        SessionStore.Save(root, new NotebookSession
        {
            Recent = _recent,
            SidebarCollapsed = collapsed,
            SidebarWidth = width > 0 ? width : SessionStore.DefaultSidebarWidth,
        });
    }

    /// <summary>Watch the notebook for external changes, refreshing the tree + search index when they land.</summary>
    private void StartWatching()
    {
        _watcher?.Dispose();
        _watcher = _notebook.RootPath is { } root ? new Shell.NotebookWatcher(root, OnNotebookFilesChanged) : null;
    }

    private void OnNotebookFilesChanged()
    {
        _sidebarView.RefreshTree();
        _ = _search?.RebuildAsync();
    }

    // ---- Notebook search (PRD §7.2.3) ----------------------------------------------------------------

    /// <summary>Create the per-notebook search service and kick off its first background index build.</summary>
    private void StartSearchIndex()
    {
        if (_search is not null)
        {
            _search.IndexUpdated -= OnSearchIndexUpdated;
        }

        _search = new Search.NotebookSearchService(_notebook);
        _search.IndexUpdated += OnSearchIndexUpdated;
        _sidebarView.ConfigureSearch(_search.Search);
        _ = _search.RebuildAsync();
    }

    private void OnSearchIndexUpdated() =>
        _sidebarView.SetSearchStatus(_search is { IsIndexing: false } s ? $"{s.FileCount} files indexed" : "Indexing…");

    // ---- Editor tabs ------------------------------------------------------------------------------

    /// <summary>
    /// Open <paramref name="path"/> in a tab, focusing an existing tab for the same file, and record it in
    /// the notebook's recent list. Unsupported types (no editor pane) never enter the recent list.
    /// </summary>
    internal async Task OpenFileAsync(string path)
    {
        await _tabHost.OpenAsync(path);

        // Record it only when it actually opened: OpenAsync no-ops on an unsupported type or a failed load,
        // leaving some other tab active — so match the now-active pane's path against the requested file.
        var full = System.IO.Path.GetFullPath(path);
        if (_tabHost.ActivePane?.Path is { } opened && string.Equals(opened, full, StringComparison.OrdinalIgnoreCase))
        {
            _recent = RecentFiles.Push(_recent, opened);
            _sidebarView.SetRecent(_notebook.RootPath, _recent);
            PersistSession();
        }
    }

    // ---- Tree mutations (PRD §7.2) ----------------------------------------------------------------
    // New/rename/delete run through the shell so name-entry and confirmation use the shared overlay
    // dialogs; the filesystem rules live in the testable NotebookMutations helper. Reached from the tree
    // (F2 / Delete / context menu) and the command palette / File menu.

    private bool CanMutateSelected() =>
        _sidebarView.SelectedTreeNode is { } node && !IsRootNode(node);

    private bool IsRootNode(NotebookNode node) =>
        _notebook.RootPath is { } root && string.Equals(node.Path, root, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder a new entry lands in: the selection if a folder, else its parent, else the notebook root.</summary>
    private string? TargetDirectory(NotebookNode? node)
    {
        if (node is { IsDirectory: true })
        {
            return node.Path;
        }

        if (node is { } file && System.IO.Path.GetDirectoryName(file.Path) is { } parent)
        {
            return parent;
        }

        return _notebook.RootPath;
    }

    private async Task PromptNewFileAsync(NotebookNode? context)
    {
        if (_overlayHost.IsOpen || TargetDirectory(context) is not { } dir)
        {
            return;
        }

        if (await PromptNameAsync("New File", initial: string.Empty, okLabel: "Create") is not { } name)
        {
            return;
        }

        await MutateAsync("Couldn't create the file", async () =>
        {
            var path = NotebookMutations.CreateFile(dir, name);
            _sidebarView.RevealInTree(path);
            await OpenFileAsync(path); // opens if it's a supported type; otherwise just reveals it
        });
    }

    private async Task PromptNewFolderAsync(NotebookNode? context)
    {
        if (_overlayHost.IsOpen || TargetDirectory(context) is not { } dir)
        {
            return;
        }

        if (await PromptNameAsync("New Folder", initial: string.Empty, okLabel: "Create") is not { } name)
        {
            return;
        }

        await MutateAsync("Couldn't create the folder", () =>
        {
            _sidebarView.RevealInTree(NotebookMutations.CreateFolder(dir, name));
            return Task.CompletedTask;
        });
    }

    private async Task PromptRenameAsync(NotebookNode node)
    {
        if (_overlayHost.IsOpen || IsRootNode(node))
        {
            return;
        }

        if (await PromptNameAsync($"Rename “{node.Name}”", initial: node.Name, okLabel: "Rename") is not { } name)
        {
            return;
        }

        await MutateAsync("Couldn't rename", () =>
        {
            var newPath = NotebookMutations.Rename(node.Path, name);
            _tabHost.RebindPaths(node.Path, newPath);      // keep any open tab pointing at the moved file
            RemapRecent(node.Path, newPath);
            _sidebarView.RevealInTree(newPath);
            return Task.CompletedTask;
        });
    }

    private async Task PromptDeleteAsync(NotebookNode node)
    {
        if (_overlayHost.IsOpen || IsRootNode(node))
        {
            return;
        }

        var kind = node.IsDirectory ? "folder" : "file";
        var confirmed = await _overlayHost.ShowModalAsync(new ConfirmDialog(
            "Delete",
            $"Delete the {kind} “{node.Name}”? This can't be undone.",
            confirmLabel: "Delete",
            denyLabel: "Cancel",
            confirmIsDefault: false)); // destructive → Enter shouldn't trigger it
        if (confirmed != true)
        {
            return;
        }

        await MutateAsync("Couldn't delete", () =>
        {
            NotebookMutations.Delete(node.Path);
            _tabHost.CloseUnder(node.Path); // the file is gone — close its tab rather than let it be re-saved
            DropRecentUnder(node.Path);
            _sidebarView.RefreshTree();
            return Task.CompletedTask;
        });
    }

    /// <summary>Remap recent entries after a rename (a file, or a folder they live under).</summary>
    private void RemapRecent(string oldPath, string newPath)
    {
        var old = System.IO.Path.GetFullPath(oldPath);
        var updated = System.IO.Path.GetFullPath(newPath);
        var changed = false;
        for (var i = 0; i < _recent.Count; i++)
        {
            if (PathScope.Remap(_recent[i], old, updated) is { } remapped)
            {
                _recent[i] = remapped;
                changed = true;
            }
        }

        if (changed)
        {
            _sidebarView.SetRecent(_notebook.RootPath, _recent);
            PersistSession();
        }
    }

    /// <summary>Drop recent entries for a deleted file, or everything under a deleted folder.</summary>
    private void DropRecentUnder(string path)
    {
        var target = System.IO.Path.GetFullPath(path);
        var kept = _recent.Where(p => !PathScope.IsUnderOrEqual(p, target)).ToList();
        if (kept.Count != _recent.Count)
        {
            _recent = kept;
            _sidebarView.SetRecent(_notebook.RootPath, _recent);
            PersistSession();
        }
    }

    /// <summary>Prompt for a name in the shared overlay, returning it trimmed, or null on cancel / empty.</summary>
    private async Task<string?> PromptNameAsync(string heading, string initial, string okLabel)
    {
        var box = new TextBox { Text = initial };
        var pending = _overlayHost.ShowModalAsync(new FormDialog(heading, box, okLabel));
        Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
        var accepted = await pending;
        return accepted == true && !string.IsNullOrWhiteSpace(box.Text) ? box.Text : null;
    }

    /// <summary>Run a filesystem mutation, surfacing any failure as a message dialog.</summary>
    private async Task MutateAsync(string errorTitle, Func<Task> mutation)
    {
        try
        {
            await mutation();
            _ = _search?.RebuildAsync(); // the notebook changed — refresh the search index in the background
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _overlayHost.ShowModalAsync(new MessageDialog(errorTitle, e.Message));
        }
    }

    private async Task SaveCurrentAsync()
    {
        // Don't save (or surface a save-failure message) while a dialog is already open — a second modal
        // on the same overlay host would clobber the first and hang its awaiter.
        if (_overlayHost.IsOpen || _tabHost.ActivePane is not { } pane)
        {
            return;
        }

        try
        {
            await pane.SaveAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _overlayHost.ShowModalAsync(new MessageDialog("Couldn't save", e.Message));
        }
    }

    /// <summary>The Save / Don't-Save / Cancel prompt the tab host shows before closing a dirty document.</summary>
    private async Task<CloseDecision> PromptCloseAsync(IEditorPane pane)
    {
        if (_overlayHost.IsOpen)
        {
            return CloseDecision.Cancel; // a dialog is already up — cancel this close rather than stack a modal
        }

        var choice = await _overlayHost.ShowModalAsync(new ChoiceDialog<CloseDecision>(
            "Unsaved changes",
            $"“{pane.Title}” has unsaved changes. Save before closing?",
            CloseDecision.Cancel,
            new Choice<CloseDecision>("Save", CloseDecision.Save, IsDefault: true),
            new Choice<CloseDecision>("Don't Save", CloseDecision.Discard),
            new Choice<CloseDecision>("Cancel", CloseDecision.Cancel, IsCancel: true)));
        return choice; // ChoiceDialog resolves Esc/backdrop to its dismiss value (Cancel)
    }

    /// <summary>Closing the window prompts for each dirty tab; hold the close until they're resolved.</summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed || !_tabHost.HasDirtyTabs)
        {
            return;
        }

        e.Cancel = true;
        if (await _tabHost.CloseAllAsync())
        {
            _closingConfirmed = true;
            Close();
        }
    }

    /// <summary>Re-query every shell command's enabled state (Save/Close need a tab; rename/delete a selection).</summary>
    private void RefreshCommandStates()
    {
        foreach (var relay in _relays.Values)
        {
            relay.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Re-query only the commands whose enablement tracks the tree selection (rename/delete). The tree
    /// raises SelectionChanged on every Up/Down/type-ahead keystroke, so refreshing just these two — rather
    /// than fanning CanExecuteChanged out to the whole catalog per keystroke — keeps navigation cheap (P6).
    /// </summary>
    private void RefreshSelectionCommands()
    {
        _relays["shell.renameEntry"].RaiseCanExecuteChanged();
        _relays["shell.deleteEntry"].RaiseCanExecuteChanged();
    }

    /// <summary>Pull the active document's outline into the sidebar's Outline panel (empty when no tab).</summary>
    private void RefreshOutline() =>
        _sidebarView.SetOutline(_tabHost.ActivePane?.GetOutline() ?? Array.Empty<OutlineItem>());

    private void UpdatePaneChrome()
    {
        RefreshCommandStates();
        RefreshOutline();
        ScanForShortcutConflicts();

        var notebookName = _notebook.Root?.Name;
        string title;
        if (_tabHost.ActivePane is { } pane)
        {
            var dirty = pane.IsDirty ? " •" : string.Empty;
            title = notebookName is null
                ? $"Puffinote — {pane.Title}{dirty}"
                : $"Puffinote — {notebookName} — {pane.Title}{dirty}";
        }
        else
        {
            title = notebookName is null ? "Puffinote" : $"Puffinote — {notebookName}";
        }

        // Guard the assignment: Changed fires on every keystroke, but Window.Title is an OS call —
        // only touch it when the text actually changes (e.g. the dirty dot appears/clears).
        if (Title != title)
        {
            Title = title;
        }
    }
}
