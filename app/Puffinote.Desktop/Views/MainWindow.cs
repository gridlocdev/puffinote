using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;
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
public partial class MainWindow : Window
{
    private readonly NotebookService _notebook;

    // Puffinote's per-user override settings (PRD §7.5), loaded once at startup. Resolved against each
    // notebook's own .puffinote/settings.json on open and handed to the tab host, which layers them over an
    // editor's own config when a note/sheet pane opens.
    private PuffinoteSettings _settings = PuffinoteSettings.LoadUser();

    // The session-only answer to "show the file tree's extensions?", set by the palette/menu command and null
    // until someone uses it — at which point it outranks the saved preference for the rest of the run. Kept here
    // rather than left in the sidebar so re-resolving the settings (which every notebook open does) doesn't
    // silently revert it. Changing the preference in the dialog clears it (see ShowSettings).
    private bool? _extensionsThisSession;

    // The user's custom shell shortcuts (PRD §7.6), edited in the shortcuts editor. BuildShellCommands
    // resolves each non-reserved command's gesture through this, so a rebound key reaches menu + palette +
    // key bindings alike. Reserved global keys are fixed and never routed through it.
    private ShortcutKeymap _keymap = ShortcutKeymap.Load();
    private readonly ColumnDefinition _sidebarColumn = new(260, GridUnitType.Pixel) { MinWidth = 160 };
    private Border _sidebar = null!;
    private GridSplitter _splitter = null!;
    private Border _sidebarHandle = null!;
    private SidebarLayout _sidebarLayout = null!;

    // The notebook-scoped recent-files list (PRD §7.2.3): promotion, rename-remap, and delete-drop all live in
    // RecentFilesList; the shell mirrors changes into the sidebar's Recent panel + quick-open and persists them
    // per notebook to .puffinote/session.json (PRD §7.1) via SessionStore — loaded on open, saved as it changes.
    private readonly RecentFilesList _recent = new();

    // The notebook's background full-text search index (PRD §7.2.3), recreated per notebook. Null until a notebook
    // is open. Rebuilt off the UI thread on open and after tree mutations.

    // Watches the notebook for external filesystem changes to refresh the tree + index live (PRD §7.1).
    private Shell.NotebookWatcher? _watcher;

    private readonly TabHost _tabHost = new();

    // Document auto-save: write every dirty tab back once the editing stops. The shell owns this rather
    // than the editors because in Puffinote the shell owns saving — each pane's SaveAsync is the editor's
    // own byte-faithful atomic write, and the tab host is what calls it.
    private readonly AutoSave _autoSave;
    private readonly OverlayHost _overlayHost = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
    };

    // Puffinote's one shell command catalog (PRD §7.4): menu, key bindings, and the palette all derive
    // from it. _relays holds one shared RelayCommand per entry so their enabled state stays in step.
    private List<AppCommand> _shellCommands = [];
    private Dictionary<string, AppCommand> _shellById = [];
    private Dictionary<string, RelayCommand> _relays = [];
    private bool _closingConfirmed;

    // The most recent tab-restore (PRD §7.3), kept only so tests can await the fire-and-forget reopen.
    private Task RestoringTabs { get; set; } = Task.CompletedTask;

    private List<(KeyGesture Gesture, RelayCommand Command)> _reservedBindings = [];
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
        SidebarForTest.FileActivated += path => _ = OpenFileAsync(path);
        SidebarForTest.SelectionChanged += _ => RefreshSelectionCommands();
        SidebarForTest.PanelActivated += id =>
        {
            if (id == "outline")
            {
                RefreshOutline();
            }
        };
        SidebarForTest.NewNoteRequested += node => _ = PromptNewNoteAsync(node);
        SidebarForTest.NewSpreadsheetRequested += node => _ = PromptNewSpreadsheetAsync(node);
        SidebarForTest.NewTaskboardRequested += node => _ = PromptNewTaskboardAsync(node);
        SidebarForTest.NewFileRequested += node => _ = PromptNewFileAsync(node);
        SidebarForTest.NewFolderRequested += node => _ = PromptNewFolderAsync(node);
        SidebarForTest.RenameRequested += node => _ = PromptRenameAsync(node);
        SidebarForTest.DeleteRequested += node => _ = PromptDeleteAsync(node);
        SidebarForTest.RevealRequested += node => _ = RevealAsync(node);
        SidebarForTest.OpenExternallyRequested += node => _ = OpenExternallyAsync(node);
        SidebarForTest.ToggleRequested += ToggleSidebar;
        SidebarForTest.SettingsRequested += ShowSettings; // the same Preferences dialog ⌘, and the View menu open

        Width = 1000;
        Height = 700;
        Title = "Puffinote";

        var palette = Tokens.Active;

        _tabHost.ConfirmClose = PromptCloseAsync;

        // Built before ApplySettings, which is where the resolved interval is pushed in — from startup,
        // from each notebook open, and from Preferences alike.
        _autoSave = new AutoSave(AutoSaveTickAsync);
        _tabHost.DocumentEdited += (_, _) => _autoSave.Notify();

        // Seed the host with the per-user layer before any notebook opens, so a preference like a hidden tab
        // bar holds from the first frame. Opening a notebook re-assigns this with its own settings layered on.
        _tabHost.Settings = _settings;
        _tabHost.Changed += (_, _) =>
        {
            UpdatePaneChrome();
            PersistSession(); // remember the open tabs + which is active per notebook (PRD §7.3) as they change
        };

        var root = new DockPanel();

        // The title bar is the topmost strip, above even the in-window menu, because on macOS it *is* the
        // window's title bar — the traffic lights are drawn over it.
        DockPanel.SetDock(TitleBarForTest, Dock.Top);
        root.Children.Add(TitleBarForTest);
        DocumentTitleBar.Extend(this);

        InstallMenu(root);
        root.Children.Add(BuildBody(palette));

        // The saved preferences, before a notebook is open: with no notebook there is no second layer to
        // resolve against, but the sidebar still has to start out drawn the way the user left it.
        ApplySettings();
        InstallSwipeGestures();

        // Stack the shared overlay host above the content so dialogs (dirty-close prompt, messages) render
        // over the whole window with a dimmed backdrop.
        Content = new Panel { Children = { root, _overlayHost } };

        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            PersistSession();
            _autoSave.Dispose();
            _watcher?.Dispose();
        };
    }

    /// <summary>
    /// Write every dirty tab back once the editing has paused — every one, not just the active one: a
    /// background tab with unsaved edits is precisely the work most likely to be lost, because nobody is
    /// looking at it to notice the dot.
    /// <para>
    /// A pane declines by itself where a save would be wrong or impossible: <c>ErrorPane</c> reports no
    /// path, and <c>PlainTextPane</c> holds its write back while the file's encoding is only a guess. A
    /// modal already up stands the whole pass down — an auto-save must never write underneath a question
    /// the user is still answering, least of all the dirty-close prompt, whose answer may be "discard".
    /// </para>
    /// <para>
    /// One failure suspends auto-save for the session and is reported once, rather than raising the same
    /// dialog every thirty seconds; the tab stays dirty, ⌘S still works, and a manual save resumes it.
    /// </para>
    /// </summary>
    private async Task AutoSaveTickAsync()
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        foreach (var tab in _tabHost.Tabs.ToList())
        {
            if (!tab.Pane.IsDirty || tab.Pane.Path is null)
            {
                continue;
            }

            try
            {
                await tab.Pane.SaveAsync();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _autoSave.Suspended = true;
                await _overlayHost.ShowModalAsync(new MessageDialog(
                    "Auto-save couldn't write the document",
                    $"{System.IO.Path.GetFileName(tab.Path)}: {e.Message}\n\n" +
                    "Auto-save is paused until you save successfully yourself (⌘S)."));
                return;
            }
        }

        // Saving a pane raises its DirtyChanged — that is how the tab strip drops the dot — and that
        // reaches the countdown through DocumentEdited, re-arming it against documents that are now
        // clean. Drop it, unless something really is still unsaved (a pane that declined, or an edit
        // made while the pass was in flight), in which case the countdown is right to be running.
        if (!_tabHost.HasDirtyTabs)
        {
            _autoSave.Cancel();
        }

        UpdatePaneChrome();
    }

    /// <summary>Run one auto-save cycle now; for tests (a DispatcherTimer can't tick headlessly).</summary>
    internal Task AutoSaveNowForTest() => _autoSave.RunAsync();

    /// <summary>Whether an auto-save is armed and waiting out the pause (test seam).</summary>
    internal bool AutoSavePendingForTest => _autoSave.IsPending;

    /// <summary>Open <paramref name="path"/> as the notebook (used by the CLI-arg path and the menu).</summary>
    public void OpenNotebook(string path) => _notebook.OpenNotebook(path);

    /// <summary>The notebook's recent-files list, most-recent first (test seam).</summary>
    internal IReadOnlyList<string> RecentForTest => _recent.Paths;

    /// <summary>Whether a modal/overlay is currently shown (test seam).</summary>
    internal bool IsOverlayOpenForTest => _overlayHost.IsOpen;

    /// <summary>The id of the sidebar's active panel (test seam).</summary>
    internal string ActiveSidebarPanelForTest => SidebarForTest.ActivePanelId;

    /// <summary>The sidebar (test seam — drive the tree in end-to-end mutation tests).</summary>
    internal Sidebar.SidebarView SidebarForTest { get; } = new();

    /// <summary>
    /// The family's unified title bar: the active document's name, centred, in the app's own chrome.
    /// (Also the test seam for asserting what it names.)
    /// </summary>
    internal DocumentTitleBar TitleBarForTest { get; } = new();

    /// <summary>Whether the sidebar is currently hidden (test seam).</summary>
    internal bool IsSidebarCollapsedForTest => _sidebarLayout.IsCollapsed;

    /// <summary>Whether the collapsed-state handle is showing (test seam).</summary>
    internal bool IsSidebarHandleVisibleForTest => _sidebarHandle.IsVisible;

    /// <summary>The handle's button — the only way back to a collapsed sidebar by pointer (test seam).</summary>
    internal Button SidebarHandleButtonForTest => (Button)_sidebarHandle.Child!;

    /// <summary>The notebook search service (test seam — await a deterministic rebuild, then query).</summary>
    internal Search.NotebookSearchService? SearchForTest { get; private set; }

    /// <summary>The active pane's editor control (test seam — assert config inheritance reached the editor).</summary>
    internal Control? ActiveEditorViewForTest => _tabHost.ActivePane?.View;

    /// <summary>Awaits the in-flight session tab-restore so a test can assert the reopened tabs (test seam).</summary>
    internal Task RestoreOpenTabsForTest => RestoringTabs;

    /// <summary>The open tabs' file paths, in strip (left-to-right) order (test seam).</summary>
    internal IReadOnlyList<string> OpenTabPathsForTest => [.. _tabHost.Tabs.Select(t => t.Path)];

    /// <summary>The zoom the tab on <paramref name="path"/> is showing, or null when no such tab (test seam).</summary>
    internal double? TabZoomForTest(string path) =>
        _tabHost.Tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase))?.Pane.Zoom;

    /// <summary>Zoom the tab on <paramref name="path"/>, as its own ⌘= would (test seam).</summary>
    internal void SetTabZoomForTest(string path, double zoom)
    {
        if (_tabHost.Tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) is { } tab)
        {
            tab.Pane.Zoom = zoom;
        }
    }

    /// <summary>The active tab's file path, or null when no tab is open (test seam).</summary>
    internal string? ActiveTabPathForTest => _tabHost.ActivePane?.Path;

    /// <summary>Whether the tab strip is currently on screen (test seam).</summary>
    internal bool IsTabStripVisibleForTest => _tabHost.IsStripVisibleForTest;

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

    /// <summary>The shell's own catalog (test seam — e.g. to check the editor claims no reserved gesture).</summary>
    internal IReadOnlyList<Puffinote.Components.Commands.AppCommand> ShellCommandsForTest => _shellCommands;

    /// <summary>Open a file into a tab, as clicking it in the tree would (test seam).</summary>
    internal Task OpenFileForTest(string path) => OpenFileAsync(path);

    private Control BuildBody(ThemePalette palette)
    {
        _sidebar = new Border
        {
            Background = palette.Surface,
            Child = SidebarForTest,
        };

        _splitter = new GridSplitter { Width = 4, Background = palette.SurfaceBorder };

        // The collapsed sidebar's handle (PRD §7.2.1 — "zero/handle width"). It shares the splitter's Auto
        // column because the two are never shown together: expanded, the column is the 4px splitter; collapsed,
        // it's this strip, sitting where the sidebar's own header button was so the affordance doesn't move.
        var handleButton = Sidebar.SidebarView.IconButton("panel-left", "Show sidebar", palette, ToggleSidebar);
        handleButton.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;

        // Level with the sidebar header's own copy of it — from the same constant, not from a number that
        // looked right. The two are one control as far as the user is concerned, so a difference of a few
        // pixels reads as the button hopping each time the sidebar is hidden or shown.
        handleButton.Margin = new Thickness(2, Sidebar.SidebarView.ChromeTopInset, 2, 0);
        _sidebarHandle = new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(0, 0, 1, 0),
            IsVisible = false,
            Child = handleButton,
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            [
                _sidebarColumn,
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            ],
        };
        Grid.SetColumn(_sidebar, 0);
        Grid.SetColumn(_splitter, 1);
        Grid.SetColumn(_sidebarHandle, 1);
        Grid.SetColumn(_tabHost, 2);
        grid.Children.Add(_sidebar);
        grid.Children.Add(_splitter);
        grid.Children.Add(_sidebarHandle);
        grid.Children.Add(_tabHost);

        _sidebarLayout = new SidebarLayout(_sidebarColumn, _sidebar, _splitter, _sidebarHandle, SessionStore.DefaultSidebarWidth);
        return grid;
    }
}
