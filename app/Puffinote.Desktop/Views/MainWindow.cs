using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
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
    private readonly PuffinoteSettings _settings = PuffinoteSettings.LoadUser();

    // The user's custom shell shortcuts (PRD §7.6), edited in the shortcuts editor. BuildShellCommands
    // resolves each non-reserved command's gesture through this, so a rebound key reaches menu + palette +
    // key bindings alike. Reserved global keys are fixed and never routed through it.
    private ShortcutKeymap _keymap = ShortcutKeymap.Load();
    private readonly ColumnDefinition _sidebarColumn = new(260, GridUnitType.Pixel) { MinWidth = 160 };
    private Border _sidebar = null!;
    private GridSplitter _splitter = null!;
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
        SidebarForTest.NewFolderRequested += node => _ = PromptNewFolderAsync(node);
        SidebarForTest.RenameRequested += node => _ = PromptRenameAsync(node);
        SidebarForTest.DeleteRequested += node => _ = PromptDeleteAsync(node);
        SidebarForTest.PinnedChanged += PersistSession;

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
        Closed += (_, _) =>
        {
            PersistSession();
            _watcher?.Dispose();
        };
    }

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

    /// <summary>The notebook search service (test seam — await a deterministic rebuild, then query).</summary>
    internal Search.NotebookSearchService? SearchForTest { get; private set; }

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
            Child = SidebarForTest,
        };

        _splitter = new GridSplitter { Width = 4, Background = palette.SurfaceBorder };

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
        Grid.SetColumn(_tabHost, 2);
        grid.Children.Add(_sidebar);
        grid.Children.Add(_splitter);
        grid.Children.Add(_tabHost);

        _sidebarLayout = new SidebarLayout(_sidebarColumn, _sidebar, _splitter, SessionStore.DefaultSidebarWidth);
        return grid;
    }
}
