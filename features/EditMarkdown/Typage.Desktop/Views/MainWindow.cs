using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Typage.Control.Rendering;
using Typage.Control.Views;
using Typage.Desktop.Commands;
using Typage.Desktop.Services;
using Typage.Engine.Document;
using Typage.Engine.Html;

namespace Typage.Desktop.Views;

/// <summary>
/// The standalone shell's single window (PRD §5 — one window, one document, one process). A thin
/// host around the embeddable <see cref="MarkdownEditorView"/>: it owns the menu, file dialogs,
/// recent files, the dirty-state guard, the status bar, and HTML export. All editing logic lives
/// in the control and engine; this layer is replaceable by the future notes-app shell.
/// </summary>
public class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MarkdownEditorView _editorView = new();
    private readonly TextBlock _statusPosition = new();
    private readonly TextBlock _statusCounts = new();
    private readonly TextBlock _statusState = new();
    private string? _currentPath;
    private TextFileFormat _format = TextFileFormat.Utf8NoBom;
    private readonly MenuItem _recentMenu = new() { Header = "Open _Recent" };
    private NativeMenuItem? _nativeRecentItem; // the macOS "Open Recent" submenu (when the native bar is used)
    private readonly CommandPalette _palette = new();
    private Dictionary<string, AppCommand> _commandsById = new();
    private FindBar? _findBar;
    private Border? _toolbar;
    private Border? _statusBar;
    private CheckItem? _toggleToolbarItem;
    private CheckItem? _toggleRawSourceItem;
    private CheckItem? _toggleFocusItem;
    private bool _focusMode;

    // External-change detection (PRD §7.1): watch the open file, and remember the exact text last
    // read-from / written-to disk so our own atomic saves are recognised and don't self-trigger.
    private FileSystemWatcher? _watcher;
    private string? _lastKnownDiskText;
    private bool _conflictPromptOpen;

    // Crash recovery (PRD §9.4): autosave the dirty buffer to a sidecar snapshot; clear it on a clean save.
    private readonly RecoveryService _recovery = new();
    private DispatcherTimer? _autosaveTimer;
    private readonly List<(AppTheme Theme, CheckItem Item)> _themeItems = new();

    /// <summary>
    /// A checkable/radio menu item that may live in either the in-window menu (Windows/Linux) or the
    /// macOS native menu bar, so the toggle/theme state-sync code stays platform-agnostic — it just
    /// sets <see cref="IsChecked"/> on whichever item the active platform built.
    /// </summary>
    private sealed class CheckItem
    {
        private readonly MenuItem? _menu;
        private readonly NativeMenuItem? _native;

        public CheckItem(MenuItem menu) => _menu = menu;

        public CheckItem(NativeMenuItem native) => _native = native;

        public bool IsChecked
        {
            set
            {
                if (_menu is not null)
                {
                    _menu.IsChecked = value;
                }

                if (_native is not null)
                {
                    _native.IsChecked = value;
                }
            }
        }
    }

    // Chrome brushes are mutated live on a theme change; the borders/text share these instances.
    private readonly SolidColorBrush _chromeBg = new(Color.FromRgb(0x25, 0x25, 0x26));
    private readonly SolidColorBrush _chromeBorder = new(Color.FromRgb(0x33, 0x33, 0x36));
    private readonly SolidColorBrush _mutedText = new(Color.FromRgb(0x9A, 0x9E, 0xA6));
    private readonly SolidColorBrush _chromeInset = new(Color.FromRgb(0x1E, 0x1E, 0x1E));

    private static readonly FilePickerFileType MarkdownType = new("Markdown")
    {
        Patterns = new[] { "*.md", "*.markdown", "*.mdown", "*.txt" },
    };

    public MainWindow(SettingsService settings)
    {
        _settings = settings;
        Width = 900;
        Height = 680;
        Title = "Typage";

        var root = new DockPanel();
        InstallMenu(root); // macOS native menu bar, or the in-window menu elsewhere; plus key bindings
        _toolbar = BuildToolbar();
        _toolbar.IsVisible = _settings.Settings.ShowToolbar; // restore the persisted toolbar visibility
        root.Children.Add(_toolbar);
        _findBar = new FindBar(_editorView, _chromeBg, _chromeBorder);
        DockPanel.SetDock(_findBar, Dock.Top);
        root.Children.Add(_findBar);
        _statusBar = BuildStatusBar();
        root.Children.Add(_statusBar);
        root.Children.Add(_editorView);

        // The command palette floats over everything as a collapsed overlay.
        Content = new Panel { Children = { root, _palette } };

        _editorView.TextChanged += (_, _) => UpdateChrome();
        _editorView.SelectionChanged += (_, _) => UpdateCaretStatus();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _autosaveTimer?.Stop();
            _recovery.Clear(); // a clean exit means there is nothing to recover next launch
            _watcher?.Dispose();
        };

        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) =>
            {
                if (_settings.Settings.Theme == AppTheme.System)
                {
                    ApplyTheme(AppTheme.System); // follow live OS light/dark changes
                }
            };
        }

        ApplyTheme(_settings.Settings.Theme);
        _editorView.Editor.IndentWidth = _settings.Settings.TabWidth;
        _editorView.Editor.ShowRawSource = _settings.Settings.ShowRawSource; // restore the raw-source default

        SetupAutosave();
        Opened += async (_, _) => await CheckForRecoveryAsync(); // offer recovery once the window is up

        RefreshRecentMenu();
        UpdateChrome();
    }

    /// <summary>The embedded editor view, exposed for the headless UI tests.</summary>
    internal MarkdownEditorView EditorView => _editorView;

    // Test hooks for the view toggles (the chrome controls are private).
    internal bool StatusBarVisibleForTest => _statusBar?.IsVisible ?? false;

    internal bool ToolbarVisibleForTest => _toolbar?.IsVisible ?? false;

    internal void ToggleFocusModeForTest() => ToggleFocusMode();

    internal void ToggleRawSourceForTest() => ToggleRawSource();

    // ----- chrome -----

    /// <summary>
    /// Build the command catalog, register one key binding per command (per-platform gesture), and install
    /// the menu: the macOS native menu bar (system bar, ⌘ key-equivalents) on macOS, or the in-window menu
    /// elsewhere (PRD §7.5 — a platform-native menu bar).
    /// </summary>
    private void InstallMenu(DockPanel root)
    {
        _commandsById = BuildCommands().ToDictionary(c => c.Id);
        RegisterCommandKeyBindings();

        if (OperatingSystem.IsMacOS())
        {
            BuildNativeMenu();
        }
        else
        {
            var menu = BuildInWindowMenu();
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Add(menu);
        }
    }

    /// <summary>
    /// One window key binding per command, using its platform gesture (⌘ on macOS, Ctrl elsewhere). The
    /// standard text-editing chords (⌘/Ctrl + A/C/V/X/Z/Y) are skipped — the focused editor or dialog text
    /// field owns them — and the Windows/Linux "Ctrl+H = Replace" alias is kept since those platforms expect
    /// it. On macOS the native menu's key-equivalents also fire these; the bindings are the cross-platform path.
    /// </summary>
    private void RegisterCommandKeyBindings()
    {
        foreach (var command in _commandsById.Values)
        {
            if (command.Shortcut is not { } shortcut || shortcut.YieldsToTextInput)
            {
                continue;
            }

            KeyBindings.Add(new KeyBinding
            {
                Gesture = shortcut.ToGesture(),
                Command = new RelayCommand(command.Execute, () => command.IsEnabled),
            });
        }

        if (!OperatingSystem.IsMacOS())
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.H, KeyModifiers.Control), // Windows/Linux convention for Replace
                Command = new RelayCommand(() => _findBar?.Show(replace: true)),
            });
        }
    }

    // ----- in-window menu (Windows/Linux) -----

    private Menu BuildInWindowMenu()
    {
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(InWindowItem("file.new"));
        file.Items.Add(InWindowItem("file.open"));
        file.Items.Add(_recentMenu);
        file.Items.Add(InWindowItem("file.save"));
        file.Items.Add(InWindowItem("file.saveAs"));
        file.Items.Add(InWindowItem("file.revert"));
        file.Items.Add(new Separator());
        file.Items.Add(InWindowItem("file.exportHtml"));
        file.Items.Add(new Separator());
        file.Items.Add(MenuLeaf("E_xit", Close));

        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(InWindowItem("edit.undo"));
        edit.Items.Add(InWindowItem("edit.redo"));
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.cut"));
        edit.Items.Add(InWindowItem("edit.copy"));
        edit.Items.Add(InWindowItem("edit.paste"));
        edit.Items.Add(InWindowItem("edit.selectAll"));
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.find"));
        edit.Items.Add(InWindowItem("edit.replace"));

        var format = new MenuItem { Header = "F_ormat" };
        format.Items.Add(InWindowItem("format.bold"));
        format.Items.Add(InWindowItem("format.italic"));
        format.Items.Add(InWindowItem("format.code"));
        format.Items.Add(InWindowItem("format.strike"));
        format.Items.Add(InWindowItem("format.link"));
        format.Items.Add(new Separator());
        format.Items.Add(InWindowItem("format.h1"));
        format.Items.Add(InWindowItem("format.h2"));
        format.Items.Add(InWindowItem("format.h3"));

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(InWindowItem("view.commandPalette"));
        view.Items.Add(new Separator());
        _toggleToolbarItem = new CheckItem(AddInWindowCheck(view, "Show _Toolbar", _settings.Settings.ShowToolbar, ToggleToolbar));
        _toggleRawSourceItem = new CheckItem(AddInWindowCheck(view, "Show Raw _Source", _settings.Settings.ShowRawSource, ToggleRawSource));
        _toggleFocusItem = new CheckItem(AddInWindowCheck(view, "_Focus Mode", _focusMode, ToggleFocusMode, _commandsById["view.toggleFocus"].Gesture));
        view.Items.Add(new Separator());

        var themeMenu = new MenuItem { Header = "T_heme" };
        foreach (var t in System.Enum.GetValues<AppTheme>())
        {
            var choice = t;
            var item = new MenuItem
            {
                Header = ThemeLabel(t),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "theme",
                IsChecked = _settings.Settings.Theme == t,
            };
            item.Click += (_, _) => SetTheme(choice);
            _themeItems.Add((t, new CheckItem(item)));
            themeMenu.Items.Add(item);
        }

        view.Items.Add(themeMenu);
        view.Items.Add(new Separator());
        view.Items.Add(InWindowItem("app.preferences"));

        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(InWindowItem("help.reference"));

        var menu = new Menu();
        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(format);
        menu.Items.Add(view);
        menu.Items.Add(help);
        return menu;
    }

    /// <summary>An in-window menu item from the catalog: its title, action (gated by IsEnabled), and displayed gesture.</summary>
    private MenuItem InWindowItem(string id)
    {
        var c = _commandsById[id];
        var item = new MenuItem { Header = c.Title, Command = new RelayCommand(c.Execute, () => c.IsEnabled) };
        if (c.Gesture is { } g)
        {
            item.InputGesture = g;
        }

        return item;
    }

    private static MenuItem MenuLeaf(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem AddInWindowCheck(MenuItem parent, string header, bool initial, Action onClick, KeyGesture? gesture = null)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = initial };
        if (gesture is not null)
        {
            item.InputGesture = gesture;
        }

        item.Click += (_, _) => onClick();
        parent.Items.Add(item);
        return item;
    }

    // ----- macOS native menu bar -----

    private void BuildNativeMenu()
    {
        var menu = new NativeMenu();

        var file = NativeSubmenu("File");
        file.Menu!.Items.Add(NativeItem("file.new"));
        file.Menu.Items.Add(NativeItem("file.open"));
        _nativeRecentItem = new NativeMenuItem("Open Recent") { Menu = new NativeMenu() };
        file.Menu.Items.Add(_nativeRecentItem);
        file.Menu.Items.Add(NativeItem("file.save"));
        file.Menu.Items.Add(NativeItem("file.saveAs"));
        file.Menu.Items.Add(NativeItem("file.revert"));
        file.Menu.Items.Add(new NativeMenuItemSeparator());
        file.Menu.Items.Add(NativeItem("file.exportHtml"));
        menu.Items.Add(file);

        var edit = NativeSubmenu("Edit");
        foreach (var id in new[] { "edit.undo", "edit.redo", null, "edit.cut", "edit.copy", "edit.paste", "edit.selectAll", null, "edit.find", "edit.replace" })
        {
            edit.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        menu.Items.Add(edit);

        var format = NativeSubmenu("Format");
        foreach (var id in new[] { "format.bold", "format.italic", "format.code", "format.strike", "format.link", null, "format.h1", "format.h2", "format.h3" })
        {
            format.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        menu.Items.Add(format);

        var view = NativeSubmenu("View");
        view.Menu!.Items.Add(NativeItem("view.commandPalette"));
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        _toggleToolbarItem = new CheckItem(AddNativeCheck(view.Menu, "Show Toolbar", _settings.Settings.ShowToolbar, ToggleToolbar));
        _toggleRawSourceItem = new CheckItem(AddNativeCheck(view.Menu, "Show Raw Source", _settings.Settings.ShowRawSource, ToggleRawSource));
        _toggleFocusItem = new CheckItem(AddNativeCheck(view.Menu, "Focus Mode", _focusMode, ToggleFocusMode, _commandsById["view.toggleFocus"].Gesture));
        view.Menu.Items.Add(new NativeMenuItemSeparator());

        var themeMenu = NativeSubmenu("Theme");
        foreach (var t in System.Enum.GetValues<AppTheme>())
        {
            var choice = t;
            var item = new NativeMenuItem(ThemeLabel(t))
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = _settings.Settings.Theme == t,
                Command = new RelayCommand(() => SetTheme(choice)),
            };
            _themeItems.Add((t, new CheckItem(item)));
            themeMenu.Menu!.Items.Add(item);
        }

        view.Menu.Items.Add(themeMenu);
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        view.Menu.Items.Add(NativeItem("app.preferences"));
        menu.Items.Add(view);

        var help = NativeSubmenu("Help");
        help.Menu!.Items.Add(NativeItem("help.reference"));
        menu.Items.Add(help);

        NativeMenu.SetMenu(this, menu);
    }

    /// <summary>A native menu item from the catalog. Standard editing chords get no key-equivalent so the
    /// focused text field/editor keeps them (otherwise macOS swallows ⌘C/⌘A/⌘Z mid-edit); they still work.</summary>
    private NativeMenuItem NativeItem(string id)
    {
        var c = _commandsById[id];
        var item = new NativeMenuItem(c.Title) { Command = new RelayCommand(c.Execute, () => c.IsEnabled) };
        if (c.Shortcut is { } s && !s.YieldsToTextInput)
        {
            item.Gesture = s.ToGesture(mac: true);
        }

        return item;
    }

    private static NativeMenuItem NativeSubmenu(string header) => new(header) { Menu = new NativeMenu() };

    private static NativeMenuItem AddNativeCheck(NativeMenu parent, string header, bool initial, Action onClick, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header)
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = initial,
            Command = new RelayCommand(onClick),
        };
        if (gesture is not null)
        {
            item.Gesture = gesture;
        }

        parent.Items.Add(item);
        return item;
    }

    private Border BuildToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Avalonia.Thickness(8, 5) };
        panel.Children.Add(ToolButton("B", "Bold", () => _editorView.Editor.ToggleBold(), bold: true));
        panel.Children.Add(ToolButton("I", "Italic", () => _editorView.Editor.ToggleItalic(), italic: true));
        panel.Children.Add(ToolButton("</>", "Inline code", () => _editorView.Editor.ToggleInlineCode()));
        panel.Children.Add(ToolButton("H1", "Heading 1", () => _editorView.Editor.ApplyHeading(1)));
        panel.Children.Add(ToolButton("H2", "Heading 2", () => _editorView.Editor.ApplyHeading(2)));

        // A "Find a tool…" entry on the right opens the Command Palette (mirrors Cellestra).
        var paletteShortcut = ShortcutFormatter.Format(Shortcut.Cmd(Key.P, KeyModifiers.Shift));
        var findTool = new Button
        {
            Content = $"Find a tool…    {paletteShortcut}",
            Margin = new Avalonia.Thickness(8, 4),
            Padding = new Avalonia.Thickness(10, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Width = 200,
            FontSize = 12,
            Foreground = _mutedText,
            Background = _chromeInset,
            BorderBrush = _chromeBorder,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Command = new RelayCommand(OpenPalette),
        };
        ToolTip.SetTip(findTool, $"Run any command by name ({paletteShortcut})");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(panel, 0);
        Grid.SetColumn(findTool, 1);
        grid.Children.Add(panel);
        grid.Children.Add(findTool);

        var border = new Border
        {
            Child = grid,
            Background = _chromeBg,
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            BorderBrush = _chromeBorder,
        };
        DockPanel.SetDock(border, Dock.Top);
        return border;
    }

    private Button ToolButton(string label, string tooltip, Action action, bool bold = false, bool italic = false)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Avalonia.Thickness(9, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(5),
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Command = new RelayCommand(action),
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private Border BuildStatusBar()
    {
        foreach (var block in new[] { _statusState, _statusPosition, _statusCounts })
        {
            block.Foreground = _mutedText;
            block.FontSize = 12;
            block.VerticalAlignment = VerticalAlignment.Center;
        }

        // State on the left; counts/encoding pushed to the right (like Cellestra's status bar).
        _statusCounts.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(12, 4),
            ColumnDefinitions = new ColumnDefinitions("Auto,16,Auto,*,Auto"),
        };
        Grid.SetColumn(_statusState, 0);
        Grid.SetColumn(_statusPosition, 2);
        Grid.SetColumn(_statusCounts, 4);
        grid.Children.Add(_statusState);
        grid.Children.Add(_statusPosition);
        grid.Children.Add(_statusCounts);

        var border = new Border
        {
            Child = grid,
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            BorderBrush = _chromeBorder,
            Background = _chromeBg,
        };
        DockPanel.SetDock(border, Dock.Bottom);
        return border;
    }

    // ----- command catalog & palette -----

    private void OpenPalette() => _palette.Open(BuildCommands());

    private static string ThemeLabel(AppTheme theme) => theme switch
    {
        AppTheme.System => "Follow System",
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        AppTheme.HighContrast => "High Contrast",
        _ => theme.ToString(),
    };

    private void SetTheme(AppTheme theme)
    {
        _settings.Settings.Theme = theme;
        ApplyTheme(theme);
        _settings.Save();
    }

    /// <summary>
    /// Apply an appearance mode (PRD §7.2): set the Fluent variant for the chrome, the matching
    /// <see cref="MarkdownTheme"/> for the editor, and re-tint the shared chrome brushes. System
    /// follows the OS; high contrast pairs a dark shell with the high-contrast editor palette.
    /// </summary>
    private void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark or AppTheme.HighContrast => ThemeVariant.Dark,
            _ => ThemeVariant.Default, // follow OS
        };

        var editorTheme = theme switch
        {
            AppTheme.Light => MarkdownTheme.Light,
            AppTheme.HighContrast => MarkdownTheme.HighContrast,
            AppTheme.Dark => MarkdownTheme.Dark,
            _ => app.ActualThemeVariant == ThemeVariant.Light ? MarkdownTheme.Light : MarkdownTheme.Dark,
        };
        _editorView.EditorTheme = editorTheme;

        var isLight = ReferenceEquals(editorTheme, MarkdownTheme.Light);
        ApplyChromeColors(isLight, theme == AppTheme.HighContrast);

        foreach (var (t, item) in _themeItems)
        {
            item.IsChecked = t == theme; // keep the View ▸ Theme radios in sync
        }
    }

    private void ApplyChromeColors(bool isLight, bool isHighContrast)
    {
        if (isHighContrast)
        {
            _chromeBg.Color = Color.FromRgb(0x0A, 0x0A, 0x0A);
            _chromeBorder.Color = Color.FromRgb(0x66, 0x66, 0x66);
            _mutedText.Color = Color.FromRgb(0xDD, 0xDD, 0xDD);
            _chromeInset.Color = Colors.Black;
        }
        else if (isLight)
        {
            _chromeBg.Color = Color.FromRgb(0xF3, 0xF3, 0xF3);
            _chromeBorder.Color = Color.FromRgb(0xD7, 0xD7, 0xD7);
            _mutedText.Color = Color.FromRgb(0x6A, 0x6E, 0x76);
            _chromeInset.Color = Colors.White;
        }
        else
        {
            _chromeBg.Color = Color.FromRgb(0x25, 0x25, 0x26);
            _chromeBorder.Color = Color.FromRgb(0x33, 0x33, 0x36);
            _mutedText.Color = Color.FromRgb(0x9A, 0x9E, 0xA6);
            _chromeInset.Color = Color.FromRgb(0x1E, 0x1E, 0x1E);
        }
    }

    private void ToggleToolbar()
    {
        var show = !(_toolbar?.IsVisible ?? true);
        if (_toolbar is not null)
        {
            _toolbar.IsVisible = show;
        }

        if (_toggleToolbarItem is not null)
        {
            _toggleToolbarItem.IsChecked = show;
        }

        _settings.Settings.ShowToolbar = show;
        _settings.Save();
    }

    /// <summary>Toggle the raw-source view; the new state also becomes the persisted default (PRD §7.4).</summary>
    private void ToggleRawSource()
    {
        var raw = !_editorView.Editor.ShowRawSource;
        _editorView.Editor.ShowRawSource = raw;
        if (_toggleRawSourceItem is not null)
        {
            _toggleRawSourceItem.IsChecked = raw;
        }

        _settings.Settings.ShowRawSource = raw;
        _settings.Save();
    }

    /// <summary>Toggle distraction-free mode: hide the toolbar and status bar (session-only).</summary>
    private void ToggleFocusMode()
    {
        _focusMode = !_focusMode;
        if (_toolbar is not null)
        {
            // In focus mode the toolbar is hidden; otherwise restore the user's toolbar preference.
            _toolbar.IsVisible = !_focusMode && _settings.Settings.ShowToolbar;
        }

        if (_statusBar is not null)
        {
            _statusBar.IsVisible = !_focusMode;
        }

        if (_toggleFocusItem is not null)
        {
            _toggleFocusItem.IsChecked = _focusMode;
        }
    }

    /// <summary>
    /// The one command catalog (mirroring Cellestra's golden rule #4): the palette reads it, and the
    /// menu/toolbar invoke the same underlying actions. Built per open so CanExecute reflects the
    /// current state.
    /// </summary>
    private List<AppCommand> BuildCommands()
    {
        var ed = _editorView.Editor;
        return new List<AppCommand>
        {
            new("file.new", "New", "Start a new, empty document.", Shortcut.Cmd(Key.N), () => _ = NewDocumentAsync()),
            new("file.open", "Open…", "Open a Markdown file from disk.", Shortcut.Cmd(Key.O), () => _ = OpenAsync()),
            new("file.save", "Save", "Save changes to the current file.", Shortcut.Cmd(Key.S), () => _ = SaveAsync()),
            new("file.saveAs", "Save As…", "Save a copy under a new name.", Shortcut.Cmd(Key.S, KeyModifiers.Shift), () => _ = SaveAsAsync()),
            new("file.revert", "Revert to Saved", "Discard changes since the last save.", null,
                () => _ = RevertAsync(), () => _currentPath is not null && _editorView.IsDirty),
            new("file.exportHtml", "Export HTML…", "Render the document to a self-contained HTML file.", null, () => _ = ExportHtmlAsync()),

            new("edit.undo", "Undo", "Step backward through your changes.", Shortcut.Cmd(Key.Z), () => _editorView.Undo(), () => _editorView.CanUndo),
            new("edit.redo", "Redo", "Re-apply a change you undid.", Shortcut.Cmd(Key.Z, KeyModifiers.Shift), () => _editorView.Redo(), () => _editorView.CanRedo),
            new("edit.cut", "Cut", "Move the selection to the clipboard.", Shortcut.Cmd(Key.X), () => _ = ed.CutAsync()),
            new("edit.copy", "Copy", "Copy the selection to the clipboard.", Shortcut.Cmd(Key.C), () => _ = ed.CopyAsync()),
            new("edit.paste", "Paste", "Paste from the clipboard.", Shortcut.Cmd(Key.V), () => _ = ed.PasteAsync()),
            new("edit.selectAll", "Select All", "Select the whole document.", Shortcut.Cmd(Key.A), ed.SelectAll),
            new("edit.find", "Find…", "Find text in the document.", Shortcut.Cmd(Key.F), () => _findBar?.Show(replace: false)),
            new("edit.replace", "Find & Replace…", "Find text and replace it (plain or regex).", Shortcut.Cmd(Key.F, KeyModifiers.Alt), () => _findBar?.Show(replace: true)),

            new("format.bold", "Bold", "Make the selection bold.", Shortcut.Cmd(Key.B), ed.ToggleBold),
            new("format.italic", "Italic", "Make the selection italic.", Shortcut.Cmd(Key.I), ed.ToggleItalic),
            new("format.code", "Inline Code", "Format the selection as inline code.", Shortcut.Cmd(Key.E), ed.ToggleInlineCode),
            new("format.strike", "Strikethrough", "Strike through the selection.", null, ed.ToggleStrikethrough),
            new("format.link", "Insert Link", "Wrap the selection as a link.", Shortcut.Cmd(Key.K), ed.InsertLink),
            new("format.h1", "Heading 1", "Make this line a level-1 heading.", Shortcut.Cmd(Key.D1), () => ed.ApplyHeading(1)),
            new("format.h2", "Heading 2", "Make this line a level-2 heading.", Shortcut.Cmd(Key.D2), () => ed.ApplyHeading(2)),
            new("format.h3", "Heading 3", "Make this line a level-3 heading.", Shortcut.Cmd(Key.D3), () => ed.ApplyHeading(3)),
            new("format.paragraph", "Paragraph", "Remove the heading from this line.", Shortcut.Cmd(Key.D0), ed.SetParagraph),

            new("view.commandPalette", "Command Palette…", "Search and run any command.", Shortcut.Cmd(Key.P, KeyModifiers.Shift), OpenPalette),
            new("view.toggleToolbar", "Toggle Toolbar", "Show or hide the formatting toolbar.", null, ToggleToolbar),
            new("view.toggleRawSource", "Toggle Raw Source", "Show the raw Markdown source (markers always visible).", null, ToggleRawSource),
            new("view.toggleFocus", "Toggle Focus Mode", "Distraction-free: hide the toolbar and status bar.", Shortcut.Bare(Key.F11), ToggleFocusMode),
            new("app.preferences", "Preferences…", "Theme, tab width, and toolbar settings.", Shortcut.Cmd(Key.OemComma), ShowSettings),
            new("help.reference", "Markdown Reference", "Open the offline syntax & shortcut reference.", null, ShowHelp),
        };
    }

    // ----- file operations -----

    public void OpenPath(string path)
    {
        try
        {
            var (text, format) = TextFileCodec.Decode(File.ReadAllBytes(path));
            _format = format; // remember the encoding/BOM so a save round-trips it
            _editorView.LoadText(text);
            _currentPath = path;
            _lastKnownDiskText = text; // the on-disk baseline for external-change detection
            _settings.Settings.PushRecent(path);
            _settings.Save();
            RefreshRecentMenu();
            UpdateChrome();
            WatchCurrentFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Couldn't open “{Path.GetFileName(path)}”.",
                $"{ex.Message}\n\nCheck that the file still exists and that you have permission to read it.");
        }
    }

    /// <summary>Rebuild the File ▸ Open Recent submenu from the persisted MRU list (heuristic 7).</summary>
    private void RefreshRecentMenu()
    {
        var recent = _settings.Settings.RecentFiles;

        // macOS native menu: refresh the submenu items in place (re-SetMenu while mounted throws on macOS).
        if (_nativeRecentItem?.Menu is { } nativeSub)
        {
            nativeSub.Items.Clear();
            if (recent.Count == 0)
            {
                nativeSub.Items.Add(new NativeMenuItem("(no recent files)") { IsEnabled = false });
                return;
            }

            foreach (var entry in recent)
            {
                var path = entry;
                nativeSub.Items.Add(new NativeMenuItem(path) { Command = new RelayCommand(() => _ = OpenRecentAsync(path)) });
            }

            nativeSub.Items.Add(new NativeMenuItemSeparator());
            nativeSub.Items.Add(new NativeMenuItem("Clear Recent") { Command = new RelayCommand(ClearRecent) });
            return;
        }

        _recentMenu.Items.Clear();
        if (recent.Count == 0)
        {
            _recentMenu.Items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
            return;
        }

        foreach (var entry in recent)
        {
            var path = entry;
            _recentMenu.Items.Add(new MenuItem { Header = path, Command = new RelayCommand(() => _ = OpenRecentAsync(path)) });
        }

        _recentMenu.Items.Add(new Separator());
        _recentMenu.Items.Add(new MenuItem { Header = "_Clear Recent", Command = new RelayCommand(ClearRecent) });
    }

    private async Task OpenRecentAsync(string path)
    {
        if (await EnsureCanDiscardAsync())
        {
            OpenPath(path);
        }
    }

    private void ClearRecent()
    {
        _settings.Settings.RecentFiles.Clear();
        _settings.Save();
        RefreshRecentMenu();
    }

    private async Task NewDocumentAsync()
    {
        if (!await EnsureCanDiscardAsync())
        {
            return;
        }

        _editorView.LoadText(string.Empty);
        _currentPath = null;
        _lastKnownDiskText = null;
        _format = TextFileFormat.Utf8NoBom; // new documents default to UTF-8 (no BOM)
        UpdateChrome();
        WatchCurrentFile(); // nothing to watch now
    }

    private async Task OpenAsync()
    {
        if (!await EnsureCanDiscardAsync())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Markdown",
            AllowMultiple = false,
            FileTypeFilter = new[] { MarkdownType, FilePickerFileTypes.All },
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            // A storage-picker await can resume off the UI thread; OpenPath touches the editor and
            // settings, so marshal it back (the same hardening as the clipboard path in the surface).
            await Dispatcher.UIThread.InvokeAsync(() => OpenPath(path));
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (_currentPath is null)
        {
            return await SaveAsAsync();
        }

        return WriteToPath(_currentPath);
    }

    private async Task<bool> SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Markdown",
            DefaultExtension = "md",
            SuggestedFileName = Path.GetFileName(_currentPath) ?? "Untitled.md",
            FileTypeChoices = new[] { MarkdownType },
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            // Marshal the write + chrome update back to the UI thread (the picker await may resume off it).
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (WriteToPath(path))
                {
                    _currentPath = path;
                    _settings.Settings.PushRecent(path);
                    _settings.Save();
                    RefreshRecentMenu();
                    UpdateChrome();
                    WatchCurrentFile(); // follow the file we just saved as
                    return true;
                }

                return false;
            });
        }

        return false;
    }

    private bool WriteToPath(string path)
    {
        try
        {
            AtomicWrite(path, TextFileCodec.Encode(_editorView.Text, _format));
            _lastKnownDiskText = _editorView.Text; // so the watcher recognises this as our own save
            _editorView.MarkSaved();
            _recovery.Clear(); // the save is durable now; drop the crash-recovery snapshot
            UpdateChrome();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Couldn't save the document.",
                $"{ex.Message}\n\nTry File ▸ Save As to write to a different location.");
            return false;
        }
    }

    private async Task RevertAsync()
    {
        if (_currentPath is null || !_editorView.IsDirty)
        {
            return;
        }

        if (await ConfirmAsync("Revert to the last saved version? Unsaved changes will be lost."))
        {
            OpenPath(_currentPath);
        }
    }

    private async Task ExportHtmlAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export HTML",
            DefaultExtension = "html",
            SuggestedFileName = (Path.GetFileNameWithoutExtension(_currentPath) ?? "Untitled") + ".html",
            FileTypeChoices = new[] { new FilePickerFileType("HTML") { Patterns = new[] { "*.html", "*.htm" } } },
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            // Read the editor text and report errors on the UI thread (the picker await may resume off it).
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var title = Path.GetFileNameWithoutExtension(path);
                var html = HtmlRenderer.RenderDocument(Engine.Document.TextDocument.FromString(_editorView.Text), title);
                try
                {
                    AtomicWrite(path, new UTF8Encoding(false).GetBytes(html)); // HTML export is always UTF-8
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ShowError("Couldn't export HTML.",
                        $"{ex.Message}\n\nCheck that the destination folder is writable.");
                }
            });
        }
    }

    private static void AtomicWrite(string path, byte[] content)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, full, overwrite: true); // atomic on the same volume (PRD §7.1)
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }

    // ----- crash recovery (PRD §9.4) -----

    private void SetupAutosave()
    {
        _autosaveTimer?.Stop();
        var seconds = _settings.Settings.AutosaveSeconds;
        if (seconds <= 0)
        {
            _autosaveTimer = null; // autosave disabled
            return;
        }

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _autosaveTimer.Tick += (_, _) => AutosaveTick();
        _autosaveTimer.Start();
    }

    /// <summary>Snapshot the buffer if it has unsaved changes, else drop any stale snapshot.</summary>
    private void AutosaveTick()
    {
        if (_editorView.IsDirty)
        {
            _recovery.Save(_currentPath, _editorView.Text);
        }
        else
        {
            _recovery.Clear();
        }
    }

    /// <summary>Run one autosave cycle; for tests (the timer can't tick headlessly).</summary>
    internal void AutosaveTickForTest() => AutosaveTick();

    private bool _recoveryChecked;

    /// <summary>On startup, if a snapshot from an unclean shutdown exists, offer to recover it (PRD §9.4).</summary>
    private async Task CheckForRecoveryAsync()
    {
        if (_recoveryChecked)
        {
            return;
        }

        _recoveryChecked = true;
        if (_recovery.TryLoad() is not { } snapshot)
        {
            return;
        }

        if (await ConfirmAsync($"Recover unsaved changes from {snapshot.SavedAtUtc.ToLocalTime():g}?", confirmLabel: "Restore", denyLabel: "Discard"))
        {
            // Re-adopt the original file (encoding + disk baseline), then overlay the recovered text as dirty.
            if (snapshot.Path is not null && File.Exists(snapshot.Path))
            {
                try
                {
                    var (diskText, format) = TextFileCodec.Decode(File.ReadAllBytes(snapshot.Path));
                    _format = format;
                    _lastKnownDiskText = diskText;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The original is gone or unreadable — recover the text as an Untitled buffer.
                }

                _currentPath = snapshot.Path;
            }

            _editorView.LoadRecovered(snapshot.Text);
            UpdateChrome();
            WatchCurrentFile();
        }
        else
        {
            _recovery.Clear();
        }
    }

    // ----- external-change detection (PRD §7.1) -----

    /// <summary>What an on-disk change to the open file means relative to the editor's state.</summary>
    internal enum DiskChange
    {
        /// <summary>The disk matches what we last read/wrote (our own save or a no-op touch).</summary>
        None,

        /// <summary>The disk changed and the buffer is clean — safe to reload silently.</summary>
        ReloadClean,

        /// <summary>The disk changed and the buffer has unsaved edits — a real conflict.</summary>
        Conflict,
    }

    /// <summary>Classify an on-disk text against the last-known disk baseline and the dirty flag.</summary>
    internal DiskChange ClassifyDiskChange(string diskText)
    {
        if (diskText == _lastKnownDiskText)
        {
            return DiskChange.None;
        }

        return _editorView.IsDirty ? DiskChange.Conflict : DiskChange.ReloadClean;
    }

    private void WatchCurrentFile()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_currentPath is null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_currentPath));
            if (dir is null)
            {
                return;
            }

            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_currentPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            // Events arrive on a thread-pool thread; marshal the handling to the UI thread.
            _watcher.Changed += (_, _) => Dispatcher.UIThread.Post(HandleExternalChange);
            _watcher.Created += (_, _) => Dispatcher.UIThread.Post(HandleExternalChange);
            _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(HandleExternalChange);
        }
        catch (Exception)
        {
            _watcher = null; // watching is best-effort; never let it break editing
        }
    }

    private async void HandleExternalChange()
    {
        if (_currentPath is null || _conflictPromptOpen)
        {
            return;
        }

        string diskText;
        try
        {
            (diskText, _) = TextFileCodec.Decode(File.ReadAllBytes(_currentPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // the writer may still hold the file mid-save; a later event will retry
        }

        switch (ClassifyDiskChange(diskText))
        {
            case DiskChange.None:
                return;
            case DiskChange.ReloadClean:
                OpenPath(_currentPath); // no local edits — pick up the new version silently
                return;
            case DiskChange.Conflict:
                _conflictPromptOpen = true;
                try
                {
                    switch (await PromptConflictAsync())
                    {
                        case ConflictChoice.Reload:
                            OpenPath(_currentPath);
                            break;
                        case ConflictChoice.SaveAs:
                            await SaveAsAsync();
                            break;
                        case ConflictChoice.KeepMine:
                            _lastKnownDiskText = diskText; // accept the new disk text as the baseline; overwrite on save
                            break;
                    }
                }
                finally
                {
                    _conflictPromptOpen = false;
                }

                return;
        }
    }

    private enum ConflictChoice
    {
        Reload,
        KeepMine,
        SaveAs,
    }

    /// <summary>
    /// Offer the three-way choice when the file changed on disk while we have unsaved edits (PRD §7.1):
    /// reload (lose local edits), keep mine (overwrite on save), or fork via Save As.
    /// </summary>
    private async Task<ConflictChoice> PromptConflictAsync()
    {
        var tcs = new TaskCompletionSource<ConflictChoice>();
        var reload = new Button { Content = "Reload" };
        var keep = new Button { Content = "Keep Mine", IsDefault = true };
        var saveAs = new Button { Content = "Save As…" };
        reload.Click += (_, _) => tcs.TrySetResult(ConflictChoice.Reload);
        keep.Click += (_, _) => tcs.TrySetResult(ConflictChoice.KeepMine);
        saveAs.Click += (_, _) => tcs.TrySetResult(ConflictChoice.SaveAs);

        var name = _currentPath is null ? "This file" : Path.GetFileName(_currentPath);
        var dialog = new Window
        {
            Title = "Typage",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{name} changed on disk, but you have unsaved changes here. " +
                               "Reload to use the version on disk, keep your changes (they’ll overwrite the file when you save), " +
                               "or save a separate copy.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { reload, saveAs, keep },
                    },
                },
            },
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(ConflictChoice.KeepMine); // closing keeps the user's work
        reload.Click += (_, _) => dialog.Close();
        keep.Click += (_, _) => dialog.Close();
        saveAs.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    // ----- unsaved-changes guard (heuristics 3, 5) -----

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_editorView.IsDirty && !_closingConfirmed)
        {
            e.Cancel = true;
            if (await EnsureCanDiscardAsync())
            {
                _closingConfirmed = true;
                Close();
            }
        }
    }

    private bool _closingConfirmed;

    private enum UnsavedChoice
    {
        Save,
        Discard,
        Cancel,
    }

    /// <summary>
    /// Guard an action that abandons the current document (new/open/close). Returns true only if it
    /// is safe to proceed: the document is clean, the user chose Don't Save, or the user chose Save
    /// and the save succeeded. Offers Save / Don't Save / Cancel (PRD §7.1; heuristics 3 & 5) so the
    /// user is never forced to lose work or to cancel and save by hand.
    /// </summary>
    private async Task<bool> EnsureCanDiscardAsync()
    {
        if (!_editorView.IsDirty)
        {
            return true;
        }

        return await PromptUnsavedAsync() switch
        {
            UnsavedChoice.Discard => true,
            UnsavedChoice.Save => await SaveAsync(), // proceed only if the save actually completed
            _ => false,                              // Cancel
        };
    }

    private async Task<UnsavedChoice> PromptUnsavedAsync()
    {
        var tcs = new TaskCompletionSource<UnsavedChoice>();
        var save = new Button { Content = "Save", IsDefault = true };
        var discard = new Button { Content = "Don't Save" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => tcs.TrySetResult(UnsavedChoice.Save);
        discard.Click += (_, _) => tcs.TrySetResult(UnsavedChoice.Discard);
        cancel.Click += (_, _) => tcs.TrySetResult(UnsavedChoice.Cancel);

        var name = _currentPath is null ? "this document" : Path.GetFileName(_currentPath);
        var dialog = new Window
        {
            Title = "Typage",
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Save changes to {name} before closing?",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { discard, cancel, save },
                    },
                },
            },
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(UnsavedChoice.Cancel);
        save.Click += (_, _) => dialog.Close();
        discard.Click += (_, _) => dialog.Close();
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    /// <summary>
    /// Show a modal error stating the OS reason and a suggested action (heuristic 9; PRD §7.1).
    /// Falls back to the status bar only if there is no visible window to own a dialog.
    /// </summary>
    private void ShowError(string message, string detail)
    {
        if (!IsVisible)
        {
            _statusState.Text = message;
            return;
        }

        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true };
        var dialog = new Window
        {
            Title = "Typage",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { ok },
                    },
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        _ = dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmAsync(string message, string confirmLabel = "Discard", string denyLabel = "Cancel")
    {
        var tcs = new TaskCompletionSource<bool>();
        var yes = new Button { Content = confirmLabel, IsDefault = false };
        var no = new Button { Content = denyLabel, IsCancel = true };
        yes.Click += (_, _) => tcs.TrySetResult(true);
        no.Click += (_, _) => tcs.TrySetResult(false);

        var dialog = new Window
        {
            Title = "Typage",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { no, yes },
                    },
                },
            },
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);
        // The button clicks record the choice (above) AND close the dialog — without this the window
        // stays open and ShowDialog never returns, so clicking appears to do nothing until you close it.
        yes.Click += (_, _) => dialog.Close();
        no.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    /// <summary>The Preferences dialog (PRD §7.9): theme, tab width, and toolbar visibility.</summary>
    private void ShowSettings()
    {
        var s = _settings.Settings;

        var themeCombo = new ComboBox
        {
            ItemsSource = new[] { ThemeLabel(AppTheme.System), ThemeLabel(AppTheme.Light), ThemeLabel(AppTheme.Dark), ThemeLabel(AppTheme.HighContrast) },
            SelectedIndex = (int)s.Theme,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var tabWidth = new NumericUpDown { Minimum = 1, Maximum = 8, Increment = 1, Value = s.TabWidth, FormatString = "0" };
        var autosave = new NumericUpDown { Minimum = 0, Maximum = 600, Increment = 5, Value = s.AutosaveSeconds, FormatString = "0" };
        var showToolbar = new CheckBox { Content = "Show the formatting toolbar", IsChecked = s.ShowToolbar };
        var rawSource = new CheckBox { Content = "Show raw Markdown source by default", IsChecked = s.ShowRawSource };

        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Avalonia.Thickness(0, 0, 0, 12),
        };
        AddSettingRow(grid, 0, "Theme", themeCombo);
        AddSettingRow(grid, 1, "Tab width", tabWidth);
        AddSettingRow(grid, 2, "Autosave (sec, 0 = off)", autosave);
        Grid.SetRow(showToolbar, 3);
        Grid.SetColumnSpan(showToolbar, 2);
        showToolbar.Margin = new Avalonia.Thickness(0, 8, 0, 0);
        grid.Children.Add(showToolbar);
        Grid.SetRow(rawSource, 4);
        Grid.SetColumnSpan(rawSource, 2);
        rawSource.Margin = new Avalonia.Thickness(0, 4, 0, 0);
        grid.Children.Add(rawSource);

        var dialog = new Window
        {
            Title = "Preferences",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(18),
                Spacing = 8,
                Children =
                {
                    grid,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, ok },
                    },
                },
            },
        };

        ok.Click += (_, _) =>
        {
            var theme = (AppTheme)System.Math.Clamp(themeCombo.SelectedIndex, 0, 3);
            s.Theme = theme;
            ApplyTheme(theme);
            s.TabWidth = (int)(tabWidth.Value ?? 4);
            _editorView.Editor.IndentWidth = s.TabWidth;
            s.ShowToolbar = showToolbar.IsChecked == true;
            if (_toolbar is not null && !_focusMode)
            {
                _toolbar.IsVisible = s.ShowToolbar;
            }

            if (_toggleToolbarItem is not null)
            {
                _toggleToolbarItem.IsChecked = s.ShowToolbar;
            }

            s.AutosaveSeconds = (int)(autosave.Value ?? 30);
            SetupAutosave(); // pick up the new interval (or disable when 0)

            s.ShowRawSource = rawSource.IsChecked == true;
            _editorView.Editor.ShowRawSource = s.ShowRawSource;
            if (_toggleRawSourceItem is not null)
            {
                _toggleRawSourceItem.IsChecked = s.ShowRawSource;
            }

            _settings.Save();
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        _ = dialog.ShowDialog(this);
    }

    private static void AddSettingRow(Grid grid, int row, string label, Avalonia.Controls.Control control)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 6, 12, 0), MinWidth = 90 };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        control.Margin = new Avalonia.Thickness(0, 6, 0, 0);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    private void ShowHelp()
    {
        var help = new Window
        {
            Title = "Markdown Reference",
            Width = 420,
            Height = 480,
            Content = new ScrollViewer
            {
                Padding = new Avalonia.Thickness(16),
                Content = new TextBlock { Text = HelpText, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace") },
            },
        };
        help.Show(this);
    }

    // ----- status / title -----

    private void UpdateChrome()
    {
        UpdateTitle();
        UpdateDocumentStatus();
        UpdateCaretStatus();
    }

    private void UpdateTitle()
    {
        var name = _currentPath is null ? "Untitled" : Path.GetFileName(_currentPath);
        Title = (_editorView.IsDirty ? "• " : string.Empty) + name + " — Typage";
    }

    /// <summary>Document-level status — recomputed only on text change (not on every caret move).</summary>
    private void UpdateDocumentStatus()
    {
        var stats = _editorView.Stats; // cached; whole-document scan only on text change
        _statusState.Text = _editorView.IsDirty ? "Modified" : "Saved";
        _statusCounts.Text =
            $"{stats.Words} words · {stats.Characters} chars · ~{stats.ReadingMinutes} min · {_format.DisplayName} · {stats.NewlineLabel}";
    }

    /// <summary>Caret/selection status — cheap, updated on every selection change.</summary>
    private void UpdateCaretStatus()
    {
        var pos = _editorView.CaretPosition;
        var selected = _editorView.SelectedCharacterCount;
        _statusPosition.Text = selected > 0
            ? $"Ln {pos.DisplayLine}, Col {pos.DisplayColumn}  ({selected} selected)"
            : $"Ln {pos.DisplayLine}, Col {pos.DisplayColumn}";
    }

    private const string HelpText =
        "Markdown quick reference\n\n" +
        "# H1 … ###### H6   headings\n" +
        "**bold**  *italic*  ~~strike~~  ==highlight==\n" +
        "`inline code`   ```fenced code```\n" +
        "- bullet   1. number   - [ ] task / - [x] done\n" +
        "> blockquote\n" +
        "[text](url)   ![alt](image)   <https://url>\n" +
        "| table | header |  with a |---|---| delimiter row\n" +
        "---   horizontal rule\n\n" +
        "Shortcuts: Ctrl+B bold · Ctrl+I italic · Ctrl+E code · Ctrl+1/2/3 heading\n" +
        "Ctrl+S save · Ctrl+O open · Ctrl+N new · Ctrl+Z/Y undo/redo";
}
