using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Views;

/// <summary>
/// Puffinote's single window (PRD §5 — one notebook, one window). Phase 0 assembles the shell: a menu
/// (macOS native bar, or an in-window bar elsewhere), a collapsible sidebar hosting the read-only file
/// tree, and a placeholder editor area. No editors are embedded yet — Phase 1 mounts the first one.
/// All chrome reads its colours from the shared <see cref="Tokens"/> so it matches the family.
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
    private readonly TextBlock _placeholder = new()
    {
        Text = "Open a notebook to browse its files.\nEditors arrive in Phase 1.",
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private ColumnDefinition _sidebarColumn = new(260, GridUnitType.Pixel) { MinWidth = 160 };
    private Border _sidebar = null!;
    private GridSplitter _splitter = null!;
    private GridLength _lastSidebarWidth = new(260, GridUnitType.Pixel);

    public MainWindow(NotebookService notebook)
    {
        _notebook = notebook;
        _notebook.NotebookOpened += (_, _) => OnNotebookOpened();

        Width = 1000;
        Height = 700;
        Title = "Puffinote";

        var palette = Tokens.Dark;
        _sidebarHeader.Foreground = palette.MutedText;
        _placeholder.Foreground = palette.MutedText;

        var root = new DockPanel();
        InstallMenu(root);
        root.Children.Add(BuildBody(palette));
        Content = root;
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

        var mainArea = new Border { Child = _placeholder };

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
        Grid.SetColumn(mainArea, 2);
        grid.Children.Add(_sidebar);
        grid.Children.Add(_splitter);
        grid.Children.Add(mainArea);
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
        var toggleSidebar = new RelayCommand(ToggleSidebar);
        var about = new RelayCommand(ShowAbout);

        var openGesture = new KeyGesture(Key.O, PrimaryModifier | KeyModifiers.Shift);
        var sidebarGesture = new KeyGesture(Key.OemBackslash, PrimaryModifier);

        // Shortcuts work whether the menu is native (macOS) or in-window (elsewhere).
        KeyBindings.Add(new KeyBinding { Gesture = openGesture, Command = openNotebook });
        KeyBindings.Add(new KeyBinding { Gesture = sidebarGesture, Command = toggleSidebar });

        if (OperatingSystem.IsMacOS())
        {
            var fileMenu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem { Header = "Open Notebook…", Command = openNotebook, Gesture = openGesture },
                },
            };
            var viewMenu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem { Header = "Toggle Sidebar", Command = toggleSidebar, Gesture = sidebarGesture },
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
                    Items = { new MenuItem { Header = "Open Notebook…", Command = openNotebook, InputGesture = openGesture } },
                },
                new MenuItem
                {
                    Header = "_View",
                    Items = { new MenuItem { Header = "Toggle Sidebar", Command = toggleSidebar, InputGesture = sidebarGesture } },
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
        _placeholder.Text = "Puffinote — a second-brain notes app.\nComposes Typage (Markdown) and Cellestra (spreadsheets).";

    private void OnNotebookOpened()
    {
        _fileTree.SetRoot(_notebook.Root);
        _sidebarHeader.Text = _notebook.Root is { } r ? r.Name.ToUpperInvariant() : "NO NOTEBOOK OPEN";
        Title = _notebook.Root is { } root ? $"Puffinote — {root.Name}" : "Puffinote";
    }
}
