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
using Puffinote.Components.Theming;
using Puffinote.Desktop.Commands;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;

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
    private readonly FileTreeView _fileTree = new();
    private readonly TextBlock _sidebarHeader = new()
    {
        Text = "NO NOTEBOOK OPEN",
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(12, 8),
    };
    private ColumnDefinition _sidebarColumn = new(260, GridUnitType.Pixel) { MinWidth = 160 };
    private Border _sidebar = null!;
    private GridSplitter _splitter = null!;
    private GridLength _lastSidebarWidth = new(260, GridUnitType.Pixel);

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
        "shell.commandPalette", "shell.openNotebook", "shell.toggleSidebar",
        "shell.closeTab", "shell.nextTab", "shell.prevTab",
    };
    private List<(KeyGesture Gesture, RelayCommand Command)> _reservedBindings = new();
    private bool _conflictWarned;
    private IEditorPane? _lastScannedPane;

    public MainWindow(NotebookService notebook)
    {
        _notebook = notebook;
        _notebook.NotebookOpened += (_, _) => OnNotebookOpened();
        _fileTree.FileActivated += node => _ = OpenFileAsync(node.Path);

        Width = 1000;
        Height = 700;
        Title = "Puffinote";

        var palette = Tokens.Dark;
        _sidebarHeader.Foreground = palette.MutedText;

        _tabHost.ConfirmClose = PromptCloseAsync;
        _tabHost.Changed += (_, _) => UpdatePaneChrome();

        var root = new DockPanel();
        InstallMenu(root);
        root.Children.Add(BuildBody(palette));

        // Stack the shared overlay host above the content so dialogs (dirty-close prompt, messages) render
        // over the whole window with a dimmed backdrop.
        Content = new Panel { Children = { root, _overlayHost } };

        Closing += OnWindowClosing;
    }

    /// <summary>Open <paramref name="path"/> as the notebook (used by the CLI-arg path and the menu).</summary>
    public void OpenNotebook(string path) => _notebook.OpenNotebook(path);

    private Control BuildBody(ThemePalette palette)
    {
        _sidebar = new Border
        {
            Background = palette.Surface,
            Child = new DockPanel
            {
                Children =
                {
                    Dock(_sidebarHeader, Avalonia.Controls.Dock.Top),
                    _fileTree,
                },
            },
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

    private static T Dock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    // ---- Command catalog → menu (macOS native / in-window), key bindings, and palette --------------

    /// <summary>Puffinote's shell catalog — defined once; the menu, key bindings, and palette all read it.</summary>
    private void BuildShellCommands()
    {
        _shellCommands = new List<AppCommand>
        {
            new("shell.commandPalette", "Command Palette", "Search and run any command", Shortcut.Cmd(Key.K),
                ShowCommandPalette, icon: "command"),
            new("shell.openNotebook", "Open Notebook…", "Open a folder as the notebook", Shortcut.Cmd(Key.O, KeyModifiers.Shift),
                () => _ = OpenNotebookInteractiveAsync(), icon: "folder-open"),
            new("shell.save", "Save", "Save the active document", Shortcut.Cmd(Key.S),
                () => _ = SaveCurrentAsync(), () => _tabHost.ActivePane is not null, icon: "save"),
            new("shell.closeTab", "Close Tab", "Close the active tab", Shortcut.Cmd(Key.W),
                () => _ = _tabHost.CloseActiveAsync(), () => _tabHost.ActivePane is not null),
            new("shell.nextTab", "Next Tab", "Switch to the next tab", Shortcut.Cmd(Key.OemCloseBrackets, KeyModifiers.Shift),
                () => _tabHost.ActivateRelative(1)),
            new("shell.prevTab", "Previous Tab", "Switch to the previous tab", Shortcut.Cmd(Key.OemOpenBrackets, KeyModifiers.Shift),
                () => _tabHost.ActivateRelative(-1)),
            new("shell.toggleSidebar", "Toggle Sidebar", "Show or hide the sidebar", Shortcut.Cmd(Key.OemBackslash),
                ToggleSidebar),
            new("shell.focusTree", "Focus File Tree", "Move keyboard focus to the file tree", null,
                () => _fileTree.FocusTree()),
            new("shell.about", "About Puffinote", "About this app", null, ShowAbout),
        };
        _shellById = _shellCommands.ToDictionary(c => c.Id);
    }

    private void InstallMenu(DockPanel root)
    {
        BuildShellCommands();

        // One shared RelayCommand per catalog entry (menu + native menu + key bindings bind to it), so
        // their enabled state stays in step — UpdatePaneChrome refreshes them together.
        _relays = _shellCommands.ToDictionary(c => c.Id, c => new RelayCommand(() => c.Execute(), () => c.IsEnabled));

        // Non-reserved shortcuts are ordinary (bubbling) key bindings — the focused editor gets first
        // refusal. Reserved shortcuts are routed on the tunnel (below) so they always win.
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
        AddHandler(KeyDownEvent, OnReservedKeyDown, RoutingStrategies.Tunnel);

        if (OperatingSystem.IsMacOS())
        {
            NativeMenu.SetMenu(this, new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem { Header = "File", Menu = BuildNativeMenu("shell.openNotebook", "shell.save", "shell.closeTab") },
                    new NativeMenuItem { Header = "View", Menu = BuildNativeMenu("shell.commandPalette", "shell.toggleSidebar", "shell.nextTab", "shell.prevTab") },
                    new NativeMenuItem { Header = "Help", Menu = BuildNativeMenu("shell.about") },
                },
            });
            return;
        }

        var menu = new Menu
        {
            Items =
            {
                new MenuItem { Header = "_File", ItemsSource = BuildMenuItems("shell.openNotebook", "shell.save", "shell.closeTab") },
                new MenuItem { Header = "_View", ItemsSource = BuildMenuItems("shell.commandPalette", "shell.toggleSidebar", "shell.nextTab", "shell.prevTab") },
                new MenuItem { Header = "_Help", ItemsSource = BuildMenuItems("shell.about") },
            },
        };
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        root.Children.Add(menu);
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
        var editor = _tabHost.ActivePane?.Commands ?? (IReadOnlyList<AppCommand>)Array.Empty<AppCommand>();
        var entries = CommandMerge.ToEntries(_shellCommands, editor);

        var palette = new PaletteListView { Placeholder = "Type a command…" };
        palette.SetItems(entries);
        palette.Chosen += entry => { _overlayHost.Close(); entry.Run(); };
        palette.Cancelled += (_, _) => _overlayHost.Close();

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

        foreach (var (gesture, command) in _reservedBindings)
        {
            if (gesture.Matches(e))
            {
                if (command.CanExecute(null))
                {
                    command.Execute(null);
                }

                e.Handled = true; // reserved: the editor never sees it
                return;
            }
        }
    }

    /// <summary>
    /// When the active document's editor first appears, warn once if any of its shortcuts collide with a
    /// reserved global key — the reserved key wins, so the editor binding is shadowed (PRD §7.6).
    /// </summary>
    private void ScanForShortcutConflicts()
    {
        if (_conflictWarned || _tabHost.ActivePane is not { } pane || ReferenceEquals(pane, _lastScannedPane))
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

        _conflictWarned = true;
        var lines = conflicts
            .Select(c => $"•  {c.Editor.ShortcutText}  {c.Editor.Title}  →  “{c.Reserved.Title}” (Puffinote)")
            .ToList();
        _ = _overlayHost.ShowModalAsync(new MessageDialog(
            "Some editor shortcuts are overridden",
            "This editor defines shortcuts that Puffinote reserves for the shell. Puffinote's shortcut "
                + "wins, so the editor's is shadowed here:",
            detail: string.Join("\n", lines) + "\n\nYou'll be able to rebind these in the shortcuts editor."));
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
    }

    private void ShowAbout() =>
        _ = _overlayHost.ShowModalAsync(new MessageDialog(
            "Puffinote",
            "A second-brain notes app — composes Typage (Markdown) and Cellestra (spreadsheets)."));

    private void OnNotebookOpened()
    {
        _fileTree.SetRoot(_notebook.Root);
        _sidebarHeader.Text = _notebook.Root is { } r ? r.Name.ToUpperInvariant() : "NO NOTEBOOK OPEN";
        UpdatePaneChrome();
    }

    // ---- Editor tabs ------------------------------------------------------------------------------

    /// <summary>Open <paramref name="path"/> in a tab, focusing an existing tab for the same file.</summary>
    internal Task OpenFileAsync(string path) => _tabHost.OpenAsync(path);

    private async Task SaveCurrentAsync()
    {
        if (_tabHost.ActivePane is not { } pane)
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

    private void UpdatePaneChrome()
    {
        foreach (var relay in _relays.Values)
        {
            relay.RaiseCanExecuteChanged(); // refresh menu/keys enabled state (e.g. Save/Close need a tab)
        }

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
