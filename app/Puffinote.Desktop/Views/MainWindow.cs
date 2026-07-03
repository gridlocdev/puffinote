using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
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
    private RelayCommand? _saveCommand;
    private bool _closingConfirmed;

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

    // ---- Menu (macOS native bar / in-window bar) + key bindings ------------------------------------

    private void InstallMenu(DockPanel root)
    {
        var openNotebook = new RelayCommand(() => _ = OpenNotebookInteractiveAsync());
        var save = new RelayCommand(() => _ = SaveCurrentAsync(), () => _tabHost.ActivePane is not null);
        _saveCommand = save;
        var closeTab = new RelayCommand(() => _ = _tabHost.CloseActiveAsync(), () => _tabHost.ActivePane is not null);
        var nextTab = new RelayCommand(() => _tabHost.ActivateRelative(1));
        var prevTab = new RelayCommand(() => _tabHost.ActivateRelative(-1));
        var toggleSidebar = new RelayCommand(ToggleSidebar);
        var about = new RelayCommand(ShowAbout);

        var openGesture = new KeyGesture(Key.O, PrimaryModifier | KeyModifiers.Shift);
        var saveGesture = new KeyGesture(Key.S, PrimaryModifier);
        var closeGesture = new KeyGesture(Key.W, PrimaryModifier);
        var nextGesture = new KeyGesture(Key.OemCloseBrackets, PrimaryModifier | KeyModifiers.Shift);
        var prevGesture = new KeyGesture(Key.OemOpenBrackets, PrimaryModifier | KeyModifiers.Shift);
        var sidebarGesture = new KeyGesture(Key.OemBackslash, PrimaryModifier);

        // Shortcuts work whether the menu is native (macOS) or in-window (elsewhere).
        KeyBindings.Add(new KeyBinding { Gesture = openGesture, Command = openNotebook });
        KeyBindings.Add(new KeyBinding { Gesture = saveGesture, Command = save });
        KeyBindings.Add(new KeyBinding { Gesture = closeGesture, Command = closeTab });
        KeyBindings.Add(new KeyBinding { Gesture = nextGesture, Command = nextTab });
        KeyBindings.Add(new KeyBinding { Gesture = prevGesture, Command = prevTab });
        KeyBindings.Add(new KeyBinding { Gesture = sidebarGesture, Command = toggleSidebar });

        if (OperatingSystem.IsMacOS())
        {
            var fileMenu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem { Header = "Open Notebook…", Command = openNotebook, Gesture = openGesture },
                    new NativeMenuItem { Header = "Save", Command = save, Gesture = saveGesture },
                    new NativeMenuItem { Header = "Close Tab", Command = closeTab, Gesture = closeGesture },
                },
            };
            var viewMenu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem { Header = "Toggle Sidebar", Command = toggleSidebar, Gesture = sidebarGesture },
                    new NativeMenuItem { Header = "Next Tab", Command = nextTab, Gesture = nextGesture },
                    new NativeMenuItem { Header = "Previous Tab", Command = prevTab, Gesture = prevGesture },
                },
            };
            var helpMenu = new NativeMenu { Items = { new NativeMenuItem { Header = "About Puffinote", Command = about } } };
            NativeMenu.SetMenu(this, new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem { Header = "File", Menu = fileMenu },
                    new NativeMenuItem { Header = "View", Menu = viewMenu },
                    new NativeMenuItem { Header = "Help", Menu = helpMenu },
                },
            });
            return;
        }

        var menu = new Menu
        {
            Items =
            {
                new MenuItem
                {
                    Header = "_File",
                    Items =
                    {
                        new MenuItem { Header = "Open Notebook…", Command = openNotebook, InputGesture = openGesture },
                        new MenuItem { Header = "Save", Command = save, InputGesture = saveGesture },
                        new MenuItem { Header = "Close Tab", Command = closeTab, InputGesture = closeGesture },
                    },
                },
                new MenuItem
                {
                    Header = "_View",
                    Items =
                    {
                        new MenuItem { Header = "Toggle Sidebar", Command = toggleSidebar, InputGesture = sidebarGesture },
                        new MenuItem { Header = "Next Tab", Command = nextTab, InputGesture = nextGesture },
                        new MenuItem { Header = "Previous Tab", Command = prevTab, InputGesture = prevGesture },
                    },
                },
                new MenuItem
                {
                    Header = "_Help",
                    Items = { new MenuItem { Header = "About Puffinote", Command = about } },
                },
            },
        };
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        root.Children.Add(menu);
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
        _saveCommand?.RaiseCanExecuteChanged();

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
