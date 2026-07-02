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
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Control.Views;
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
    // Hosts internal modal dialogs (confirm prompts, etc.) over a dimmed backdrop, instead of native
    // OS dialog windows — the shared host the editors share.
    private readonly OverlayHost _overlayHost = new() { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
    private Dictionary<string, AppCommand> _commandsById = new();
    private FindBar? _findBar;
    private Border? _toolbar;
    private Border? _statusBar;
    private CheckItem? _toggleToolbarItem;
    private CheckItem? _toggleFocusItem;
    private readonly List<(MarkdownEditor.ViewMode Mode, CheckItem Item)> _viewModeItems = new();
    private bool _focusMode;

    // External-change detection (PRD §7.1): watch the open file, and remember the exact text last
    // read-from / written-to disk so our own atomic saves are recognised and don't self-trigger.
    private FileSystemWatcher? _watcher;
    private string? _lastKnownDiskText;
    private bool _conflictPromptOpen;

    // Crash recovery (PRD §9.4): autosave the dirty buffer to a sidecar snapshot; clear it on a clean save.
    private readonly RecoveryService _recovery = new();
    private DispatcherTimer? _autosaveTimer;
    private DispatcherTimer? _statsTimer; // debounces the whole-document word/char scan while typing
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
    private readonly SolidColorBrush _drawerHover = new(Color.FromArgb(0x22, 0x88, 0x88, 0x88));

    private static readonly FilePickerFileType MarkdownType = new("Markdown")
    {
        Patterns = new[] { "*.md", "*.markdown", "*.mdown", "*.txt" },
    };

    private static readonly FilePickerFileType ImageType = new("Images")
    {
        Patterns = ImageStaging.ImagePatterns,
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

        // The command palette and the modal overlay host float over everything; the host sits on top so
        // a dialog renders above the palette. When a modal closes, return focus to the editor.
        Content = new Panel { Children = { root, _palette, _overlayHost } };
        _overlayHost.Closed += (_, _) => _editorView.Focus();

        _editorView.TextChanged += (_, _) =>
        {
            // The title + caret status are cheap; refresh them now. The word/char counts scan the whole
            // document, so debounce them — recompute once typing pauses instead of on every keystroke.
            UpdateTitle();
            UpdateCaretStatus();
            ScheduleDocumentStatus();
        };
        _editorView.SelectionChanged += (_, _) => UpdateCaretStatus();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _autosaveTimer?.Stop();
            _statsTimer?.Stop();
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
        _editorView.Editor.View = _settings.Settings.View; // restore the view-mode default
        _editorView.Editor.CompletedTaskSorting = _settings.Settings.CompletedTaskSort; // restore task-sort default
        _editorView.Editor.HorizontalPadding = _settings.Settings.EditorPadding; // restore editor padding
        _editorView.Editor.VerticalPadding = _settings.Settings.EditorPaddingVertical;

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

    internal void SetViewModeForTest(MarkdownEditor.ViewMode mode) => SetViewMode(mode);

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
                Command = CommandFor(command),
            });
        }

        if (!OperatingSystem.IsMacOS())
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.H, KeyModifiers.Control), // Windows/Linux convention for Replace
                Command = new RelayCommand(() => _findBar?.Show(replace: true), () => !AnyOverlayOpen),
            });
        }
    }

    /// <summary>True while a palette or an internal modal is open over the content.</summary>
    private bool AnyOverlayOpen => _overlayHost.IsOpen || _palette.IsVisible;

    /// <summary>
    /// The ICommand backing a command's shortcut / menu item. Gated on <see cref="AnyOverlayOpen"/> so a
    /// keyboard shortcut (or a macOS native-menu accelerator, which the overlay scrim can't intercept)
    /// can't fire a command while a palette or modal dialog is up.
    /// </summary>
    private RelayCommand CommandFor(AppCommand command) =>
        new(command.Execute, () => command.IsEnabled && !AnyOverlayOpen);

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
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.duplicateLine"));
        edit.Items.Add(InWindowItem("edit.deleteLine"));
        edit.Items.Add(InWindowItem("edit.moveLineUp"));
        edit.Items.Add(InWindowItem("edit.moveLineDown"));
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.selectAll"));
        edit.Items.Add(InWindowItem("edit.selectWord"));
        edit.Items.Add(InWindowItem("edit.selectParagraph"));
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.find"));
        edit.Items.Add(InWindowItem("edit.replace"));

        var format = new MenuItem { Header = "F_ormat" };
        format.Items.Add(InWindowItem("format.bold"));
        format.Items.Add(InWindowItem("format.italic"));
        format.Items.Add(InWindowItem("format.strike"));
        format.Items.Add(InWindowItem("format.highlight"));
        format.Items.Add(InWindowItem("format.code"));
        format.Items.Add(new Separator());
        format.Items.Add(InWindowSubmenu("_Heading",
            "format.h1", "format.h2", "format.h3", "format.h4", "format.h5", "format.h6", null, "format.paragraph"));
        format.Items.Add(InWindowItem("format.quote"));
        format.Items.Add(InWindowItem("format.bulletList"));
        format.Items.Add(InWindowItem("format.numberedList"));
        format.Items.Add(InWindowItem("format.task"));

        var insert = new MenuItem { Header = "_Insert" };
        insert.Items.Add(InWindowItem("format.link"));
        insert.Items.Add(InWindowItem("insert.image"));
        insert.Items.Add(new Separator());
        insert.Items.Add(InWindowItem("insert.codeBlock"));
        insert.Items.Add(InWindowItem("insert.hr"));
        insert.Items.Add(InWindowItem("format.task"));

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(InWindowItem("view.commandPalette"));
        view.Items.Add(new Separator());
        _toggleToolbarItem = new CheckItem(AddInWindowCheck(view, "Show _Toolbar", _settings.Settings.ShowToolbar, ToggleToolbar));
        _toggleFocusItem = new CheckItem(AddInWindowCheck(view, "_Focus Mode", _focusMode, ToggleFocusMode, _commandsById["view.toggleFocus"].Gesture));
        view.Items.Add(new Separator());
        foreach (var m in System.Enum.GetValues<MarkdownEditor.ViewMode>())
        {
            var choice = m;
            var item = new MenuItem
            {
                Header = ViewModeLabel(m),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "viewmode",
                IsChecked = _settings.Settings.View == m,
            };
            item.Click += (_, _) => SetViewMode(choice);
            _viewModeItems.Add((m, new CheckItem(item)));
            view.Items.Add(item);
        }

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
        menu.Items.Add(insert);
        menu.Items.Add(view);
        menu.Items.Add(help);
        return menu;
    }

    /// <summary>An in-window menu item from the catalog: its title, action (gated by IsEnabled), displayed
    /// gesture, and a hover tooltip carrying the command's plain-language description (mirrors Cellestra).</summary>
    private MenuItem InWindowItem(string id)
    {
        var c = _commandsById[id];
        var item = new MenuItem { Header = c.Title, Command = CommandFor(c) };
        if (c.Gesture is { } g)
        {
            item.InputGesture = g;
        }

        ToolTip.SetTip(item, c.Description);
        return item;
    }

    /// <summary>A nested in-window submenu (a popout) holding the given catalog command items / separators.</summary>
    private MenuItem InWindowSubmenu(string header, params string?[] ids)
    {
        var sub = new MenuItem { Header = header };
        foreach (var id in ids)
        {
            sub.Items.Add(id is null ? new Separator() : InWindowItem(id));
        }

        return sub;
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
        foreach (var id in new[] { "edit.undo", "edit.redo", null, "edit.cut", "edit.copy", "edit.paste", null, "edit.duplicateLine", "edit.deleteLine", "edit.moveLineUp", "edit.moveLineDown", null, "edit.selectAll", "edit.selectWord", "edit.selectParagraph", null, "edit.find", "edit.replace" })
        {
            edit.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        menu.Items.Add(edit);

        var format = NativeSubmenu("Format");
        foreach (var id in new[] { "format.bold", "format.italic", "format.strike", "format.highlight", "format.code", null })
        {
            format.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        var headings = NativeSubmenu("Heading");
        foreach (var id in new[] { "format.h1", "format.h2", "format.h3", "format.h4", "format.h5", "format.h6", null, "format.paragraph" })
        {
            headings.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        format.Menu!.Items.Add(headings);
        foreach (var id in new[] { "format.quote", "format.bulletList", "format.numberedList", "format.task" })
        {
            format.Menu!.Items.Add(NativeItem(id));
        }

        menu.Items.Add(format);

        var insert = NativeSubmenu("Insert");
        foreach (var id in new[] { "format.link", "insert.image", null, "insert.codeBlock", "insert.hr", "format.task" })
        {
            insert.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        menu.Items.Add(insert);

        var view = NativeSubmenu("View");
        view.Menu!.Items.Add(NativeItem("view.commandPalette"));
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        _toggleToolbarItem = new CheckItem(AddNativeCheck(view.Menu, "Show Toolbar", _settings.Settings.ShowToolbar, ToggleToolbar));
        _toggleFocusItem = new CheckItem(AddNativeCheck(view.Menu, "Focus Mode", _focusMode, ToggleFocusMode, _commandsById["view.toggleFocus"].Gesture));
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        foreach (var m in System.Enum.GetValues<MarkdownEditor.ViewMode>())
        {
            var choice = m;
            var item = new NativeMenuItem(ViewModeLabel(m))
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = _settings.Settings.View == m,
                Command = new RelayCommand(() => SetViewMode(choice)),
            };
            _viewModeItems.Add((m, new CheckItem(item)));
            view.Menu.Items.Add(item);
        }

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
        var item = new NativeMenuItem(c.Title) { Command = CommandFor(c) };
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

        // Cellestra-style popout groups: a button opens a rich drawer of related commands (icon + title +
        // description + shortcut), so the common Markdown controls are one click away from the toolbar.
        panel.Children.Add(ToolGroup("Text", "Text",
            ("format.bold", "B"), ("format.italic", "I"), ("format.strike", "S"),
            ("format.highlight", "▰"), ("format.code", "‹›")));
        panel.Children.Add(ToolGroup("Heading", "Heading",
            ("format.h1", "H1"), ("format.h2", "H2"), ("format.h3", "H3"),
            ("format.h4", "H4"), ("format.h5", "H5"), ("format.h6", "H6"), ("format.paragraph", "¶")));
        panel.Children.Add(ToolGroup("List", "List",
            ("format.bulletList", "•"), ("format.numberedList", "1."), ("format.task", "☑"), ("format.quote", "❝")));
        panel.Children.Add(ToolGroup("Insert", "Insert",
            ("format.link", "🔗"), ("insert.image", "▢"), ("insert.codeBlock", "{ }"), ("insert.hr", "—")));

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

        // A sans-serif capital "I" is just a vertical stroke, so italicised it reads as a bare diagonal
        // line. A serif face gives the glyph its top/bottom bars, so the italic "I" is recognisable.
        if (italic)
        {
            button.FontFamily = new FontFamily("Georgia, Times New Roman, serif");
            button.FontSize = 15;
        }

        ToolTip.SetTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// A toolbar button that opens a Cellestra-style popout drawer of related commands. Each
    /// <paramref name="items"/> entry is a (command id, leading glyph) pair; the drawer shows a section
    /// header then one rich row per command (glyph + title + muted description + shortcut hint).
    /// </summary>
    private Button ToolGroup(string label, string sectionTitle, params (string Id, string Glyph)[] items)
    {
        var caret = new TextBlock { Text = "⌄", FontSize = 11, Margin = new Avalonia.Thickness(4, 0, 0, 0), Foreground = _mutedText, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(caret);

        var button = new Button
        {
            Content = content,
            Padding = new Avalonia.Thickness(9, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(5),
        };

        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        flyout.Content = BuildCommandDrawer(sectionTitle, items, () => flyout.Hide());
        button.Flyout = flyout;
        ToolTip.SetTip(button, $"{sectionTitle} tools");
        return button;
    }

    /// <summary>Build the popout drawer panel: a section header followed by a rich row per command.</summary>
    private Avalonia.Controls.Control BuildCommandDrawer(string sectionTitle, (string Id, string Glyph)[] items, Action close)
    {
        var panel = new StackPanel { Spacing = 2, MinWidth = 256, MaxWidth = 340, Margin = new Avalonia.Thickness(4) };
        panel.Children.Add(new TextBlock
        {
            Text = sectionTitle,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13.5,
            Margin = new Avalonia.Thickness(2, 0, 0, 6),
        });

        foreach (var (id, glyph) in items)
        {
            if (_commandsById.TryGetValue(id, out var command))
            {
                panel.Children.Add(BuildDrawerRow(command, glyph, close));
            }
        }

        return panel;
    }

    /// <summary>One rich drawer row: a leading glyph, the command's title + description, and its shortcut.</summary>
    private Avalonia.Controls.Control BuildDrawerRow(AppCommand command, string glyph, Action close)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 12.5,
            Width = 26,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 10, 0),
        };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = command.Title, FontSize = 13, FontWeight = FontWeight.Medium },
                new TextBlock { Text = command.Description, FontSize = 11, Foreground = _mutedText, TextWrapping = TextWrapping.Wrap },
            },
        };

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        layout.Children.Add(icon);
        if (command.ShortcutText.Length > 0)
        {
            var shortcut = new TextBlock
            {
                Text = command.ShortcutText,
                FontSize = 10.5,
                Foreground = _mutedText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0, 0, 0),
            };
            DockPanel.SetDock(shortcut, Dock.Right);
            layout.Children.Add(shortcut);
        }

        layout.Children.Add(text);

        var row = new Button
        {
            Content = layout,
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(9, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Command = new RelayCommand(() =>
            {
                if (command.IsEnabled)
                {
                    command.Execute();
                }

                close();
                _editorView.Editor.Focus();
            }, () => command.IsEnabled),
        };
        ToolTip.SetTip(row, command.Description);
        row.PointerEntered += (_, _) => row.Background = _drawerHover;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    /// <summary>The number of toolbar buttons that open a popout drawer (the Cellestra-style groups); for tests.</summary>
    internal int ToolGroupCountForTest() =>
        _toolbar?.Child is Grid grid
            ? grid.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>()).Count(b => b.Flyout is not null)
            : 0;

    /// <summary>The number of command rows a drawer built for <paramref name="ids"/> produces; for tests.</summary>
    internal int DrawerRowCountForTest(params string[] ids) =>
        ((StackPanel)BuildCommandDrawer("Test", ids.Select(i => (i, "•")).ToArray(), () => { }))
            .Children.OfType<Button>().Count();

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

    /// <summary>The menu/command label for a view mode (also used for the Preferences default; PRD §7.4).</summary>
    private static string ViewModeLabel(MarkdownEditor.ViewMode mode) => mode switch
    {
        MarkdownEditor.ViewMode.Rich => "View Rich View",
        MarkdownEditor.ViewMode.Formatted => "View Formatted View",
        _ => "View Raw Text",
    };

    /// <summary>Switch the editing surface to <paramref name="mode"/>; the new mode becomes the persisted default (PRD §7.4).</summary>
    private void SetViewMode(MarkdownEditor.ViewMode mode)
    {
        _editorView.Editor.View = mode;
        _settings.Settings.View = mode;
        _settings.Save();
        foreach (var (m, item) in _viewModeItems)
        {
            item.IsChecked = m == mode;
        }
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
            new("edit.moveLineUp", "Move Line Up", "Move the current line or block up.", Shortcut.Bare(Key.Up, KeyModifiers.Alt | KeyModifiers.Shift), ed.MoveLineUp),
            new("edit.moveLineDown", "Move Line Down", "Move the current line or block down.", Shortcut.Bare(Key.Down, KeyModifiers.Alt | KeyModifiers.Shift), ed.MoveLineDown),
            new("edit.duplicateLine", "Duplicate Line", "Duplicate the current line or selection.", Shortcut.Cmd(Key.D), ed.DuplicateLine),
            new("edit.deleteLine", "Delete Line", "Delete the current line or selection.", Shortcut.Cmd(Key.K, KeyModifiers.Shift), ed.DeleteLine),
            new("edit.selectWord", "Select Word", "Select the word under the caret.", null, ed.SelectCurrentWord),
            new("edit.selectParagraph", "Select Paragraph", "Select the paragraph under the caret.", null, ed.SelectCurrentParagraph),

            new("format.bold", "Bold", "Make the selection bold.", Shortcut.Cmd(Key.B), ed.ToggleBold),
            new("format.italic", "Italic", "Make the selection italic.", Shortcut.Cmd(Key.I), ed.ToggleItalic),
            new("format.code", "Inline Code", "Format the selection as inline code.", Shortcut.Cmd(Key.E), ed.ToggleInlineCode),
            new("format.strike", "Strikethrough", "Strike through the selection.", Shortcut.Cmd(Key.X, KeyModifiers.Shift), ed.ToggleStrikethrough),
            new("format.highlight", "Highlight", "Highlight the selection (==text==).", Shortcut.Cmd(Key.H, KeyModifiers.Shift), ed.ToggleHighlight),
            new("format.link", "Insert Link…", "Insert a link with display text and a location.", Shortcut.Cmd(Key.K), () => _ = ShowLinkDialogAsync()),
            new("format.task", "Checkbox", "Add a task checkbox to this line, or toggle it.", Shortcut.Cmd(Key.L), ed.ToggleTask),
            new("format.quote", "Blockquote", "Toggle a blockquote on the line(s).", null, ed.ToggleBlockquote),
            new("format.bulletList", "Bulleted List", "Toggle a bulleted list on the line(s).", Shortcut.Cmd(Key.D8, KeyModifiers.Shift), ed.ToggleBulletList),
            new("format.numberedList", "Numbered List", "Toggle a numbered list on the line(s).", Shortcut.Cmd(Key.D7, KeyModifiers.Shift), ed.ToggleNumberedList),
            new("format.h1", "Heading 1", "Make this line a level-1 heading.", Shortcut.Cmd(Key.D1), () => ed.ApplyHeading(1)),
            new("format.h2", "Heading 2", "Make this line a level-2 heading.", Shortcut.Cmd(Key.D2), () => ed.ApplyHeading(2)),
            new("format.h3", "Heading 3", "Make this line a level-3 heading.", Shortcut.Cmd(Key.D3), () => ed.ApplyHeading(3)),
            new("format.h4", "Heading 4", "Make this line a level-4 heading.", Shortcut.Cmd(Key.D4), () => ed.ApplyHeading(4)),
            new("format.h5", "Heading 5", "Make this line a level-5 heading.", Shortcut.Cmd(Key.D5), () => ed.ApplyHeading(5)),
            new("format.h6", "Heading 6", "Make this line a level-6 heading.", Shortcut.Cmd(Key.D6), () => ed.ApplyHeading(6)),
            new("format.paragraph", "Paragraph", "Remove the heading from this line.", Shortcut.Cmd(Key.D0), ed.SetParagraph),

            new("insert.image", "Image…", "Pick an image file to insert.", null, () => _ = InsertImageAsync()),
            new("insert.codeBlock", "Code Block", "Insert a fenced code block.", Shortcut.Cmd(Key.E, KeyModifiers.Shift), ed.InsertCodeBlock),
            new("insert.hr", "Horizontal Rule", "Insert a horizontal rule.", null, ed.InsertHorizontalRule),

            new("view.commandPalette", "Command Palette…", "Search and run any command.", Shortcut.Cmd(Key.P, KeyModifiers.Shift), OpenPalette),
            new("view.toggleToolbar", "Toggle Toolbar", "Show or hide the formatting toolbar.", null, ToggleToolbar),
            new("view.viewRich", "View Rich View", "Live preview: formatted, with Markdown markers hidden.", null, () => SetViewMode(MarkdownEditor.ViewMode.Rich)),
            new("view.viewFormatted", "View Formatted View", "Formatted, with the raw Markdown markers revealed.", null, () => SetViewMode(MarkdownEditor.ViewMode.Formatted)),
            new("view.viewRaw", "View Raw Text", "Plain-text source: no rich formatting or decorations.", null, () => SetViewMode(MarkdownEditor.ViewMode.RawText)),
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

    // ----- image insertion + staging (PRD §8.3 images) -----

    /// <summary>
    /// Pick an image file and insert a reference to it. A <b>saved</b> document references the picked image
    /// by a relative path (it has a folder to anchor against). An <b>unsaved</b> document has no such anchor,
    /// so the image is copied into a scratch directory and referenced by that absolute staged path; the next
    /// save offers to move it into the document's permanent home (see <see cref="ReconcileStagedImagesAsync"/>).
    /// </summary>
    private async Task InsertImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert Image",
            AllowMultiple = false,
            FileTypeFilter = new[] { ImageType, FilePickerFileTypes.All },
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } source)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var ed = _editorView.Editor;
            var alt = Path.GetFileNameWithoutExtension(source);
            try
            {
                var path = _currentPath is { } docPath
                    ? ImageStaging.RelativeUrl(docPath, source)              // saved → relative path in place
                    : ImageStaging.Stage(source, System.Guid.NewGuid().ToString("N")); // unsaved → stage a copy
                ed.InsertImageReference(ImageStaging.EncodeUrl(path), alt); // encode so spaces don't break the link
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError("Couldn't insert the image.",
                    $"{ex.Message}\n\nThe image could not be staged for the unsaved document.");
            }
        });
    }

    /// <summary>
    /// When the document being saved to <paramref name="targetPath"/> still references staged (scratch)
    /// images, offer to copy them into an <c>images/</c> folder beside the document (created if missing) and
    /// rewrite the Markdown to point there relatively. The files are physically copied out of the scratch
    /// directory, not just re-pathed. Declining leaves the staged paths intact (the document still opens).
    /// Runs before the bytes are written so the saved file already carries the permanent paths.
    /// </summary>
    private async Task ReconcileStagedImagesAsync(string targetPath)
    {
        var staged = ImageStaging.StagedReferences(_editorView.Text);
        if (staged.Count == 0)
        {
            return;
        }

        var noun = staged.Count == 1 ? "image" : "images";
        if (!await ConfirmAsync(
                $"Copy {staged.Count} staged {noun} into an \"images\" folder next to the document, and update the links?",
                confirmLabel: "Copy", denyLabel: "Keep As-Is"))
        {
            return;
        }

        var (newText, error) = ImageStaging.Reconcile(_editorView.Text, targetPath, staged);
        if (error is not null)
        {
            ShowError("Couldn't copy some staged images.",
                $"{error}\n\nThe document will be saved with its current image links.");
        }

        if (newText != _editorView.Text)
        {
            var buffer = _editorView.Editor.Buffer;
            buffer.ReplaceRange(0, buffer.Document.Length, newText);
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (_currentPath is null)
        {
            return await SaveAsAsync();
        }

        await ReconcileStagedImagesAsync(_currentPath);
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
            // Hop to the UI thread (the picker await may resume off it) before the modal + editor edits.
            await Dispatcher.UIThread.InvokeAsync(() => { });

            // Offer to move any staged images into their permanent home before the bytes hit disk.
            await ReconcileStagedImagesAsync(path);

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
        var name = _currentPath is null ? "This file" : Path.GetFileName(_currentPath);
        // Esc / backdrop keep the user's work (the safe default), same as closing the old window did.
        return await _overlayHost.ShowModalAsync(new ChoiceDialog<ConflictChoice>(
            heading: string.Empty,
            message: $"{name} changed on disk, but you have unsaved changes here. " +
                     "Reload to use the version on disk, keep your changes (they’ll overwrite the file when you save), " +
                     "or save a separate copy.",
            dismiss: ConflictChoice.KeepMine,
            new Choice<ConflictChoice>("Reload", ConflictChoice.Reload),
            new Choice<ConflictChoice>("Save As…", ConflictChoice.SaveAs),
            new Choice<ConflictChoice>("Keep Mine", ConflictChoice.KeepMine, IsDefault: true)));
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
        var name = _currentPath is null ? "this document" : Path.GetFileName(_currentPath);
        // Esc / backdrop cancel the close, never silently discarding or saving.
        return await _overlayHost.ShowModalAsync(new ChoiceDialog<UnsavedChoice>(
            heading: string.Empty,
            message: $"Save changes to {name} before closing?",
            dismiss: UnsavedChoice.Cancel,
            new Choice<UnsavedChoice>("Don't Save", UnsavedChoice.Discard),
            new Choice<UnsavedChoice>("Cancel", UnsavedChoice.Cancel, IsCancel: true),
            new Choice<UnsavedChoice>("Save", UnsavedChoice.Save, IsDefault: true)));
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

        // The bold message becomes the modal heading; the OS reason / suggested action is the body.
        _ = _overlayHost.ShowModalAsync(new MessageDialog(heading: message, message: detail));
    }

    /// <summary>
    /// The ⌘K link dialog: a "Display text" field (prefilled from the selection / word under the caret) and a
    /// "Location" field, with Cancel / Add. On Add the link is inserted over the current target; spaces in the
    /// location are encoded as <c>%20</c> so the URL survives as one Markdown destination.
    /// </summary>
    private async Task ShowLinkDialogAsync()
    {
        var ed = _editorView.Editor;
        var entered = await _overlayHost.ShowModalAsync(new LinkDialog(ed.SuggestedLinkText()));
        if (entered is not { } values)
        {
            return;
        }

        ed.InsertLinkWithText(values.Text, values.Url);
    }

    /// <summary>
    /// A yes/no prompt shown as an internal modal in the shared overlay host (no separate OS window).
    /// Confirm is not the default button, and Esc / backdrop deny — so a stray Enter can't trigger a
    /// destructive action. Returns true only when the user confirms.
    /// </summary>
    private async Task<bool> ConfirmAsync(string message, string confirmLabel = "Discard", string denyLabel = "Cancel") =>
        await _overlayHost.ShowModalAsync(
            new ConfirmDialog(heading: string.Empty, message: message, confirmLabel: confirmLabel, denyLabel: denyLabel, confirmIsDefault: false));

    /// <summary>The Preferences dialog (PRD §7.9): theme, tab width, and toolbar visibility.</summary>
    private async void ShowSettings()
    {
        var s = _settings.Settings;

        var themeCombo = new ComboBox
        {
            ItemsSource = new[] { ThemeLabel(AppTheme.System), ThemeLabel(AppTheme.Light), ThemeLabel(AppTheme.Dark), ThemeLabel(AppTheme.HighContrast) },
            SelectedIndex = (int)s.Theme,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var tabWidth = new NumericUpDown { Minimum = 1, Maximum = 8, Increment = 1, Value = s.TabWidth, FormatString = "0" };
        var padding = new NumericUpDown { Minimum = 0, Maximum = 200, Increment = 4, Value = s.EditorPadding, FormatString = "0" };
        var paddingY = new NumericUpDown { Minimum = 0, Maximum = 200, Increment = 4, Value = s.EditorPaddingVertical, FormatString = "0" };
        var autosave = new NumericUpDown { Minimum = 0, Maximum = 600, Increment = 5, Value = s.AutosaveSeconds, FormatString = "0" };
        var completedTasks = new ComboBox
        {
            ItemsSource = new[] { "Leave in place", "Move to top", "Move to bottom" },
            SelectedIndex = (int)s.CompletedTaskSort,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var viewMode = new ComboBox
        {
            ItemsSource = new[]
            {
                ViewModeLabel(MarkdownEditor.ViewMode.Rich),
                ViewModeLabel(MarkdownEditor.ViewMode.Formatted),
                ViewModeLabel(MarkdownEditor.ViewMode.RawText),
            },
            SelectedIndex = (int)s.View,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var showToolbar = new CheckBox { Content = "Show the formatting toolbar", IsChecked = s.ShowToolbar };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Width = 340,
        };
        AddSettingRow(grid, 0, "Theme", themeCombo);
        AddSettingRow(grid, 1, "Tab width", tabWidth);
        AddSettingRow(grid, 2, "Horizontal padding (px)", padding);
        AddSettingRow(grid, 3, "Vertical padding (px)", paddingY);
        AddSettingRow(grid, 4, "Autosave (sec, 0 = off)", autosave);
        AddSettingRow(grid, 5, "Completed tasks", completedTasks);
        AddSettingRow(grid, 6, "Default view", viewMode);
        Grid.SetRow(showToolbar, 7);
        Grid.SetColumnSpan(showToolbar, 2);
        showToolbar.Margin = new Avalonia.Thickness(0, 8, 0, 0);
        grid.Children.Add(showToolbar);

        if (!await _overlayHost.ShowModalAsync(new FormDialog("Preferences", grid)))
        {
            return; // Cancel / Esc — leave settings untouched
        }

        var theme = (AppTheme)System.Math.Clamp(themeCombo.SelectedIndex, 0, 3);
        s.Theme = theme;
        ApplyTheme(theme);
        s.TabWidth = (int)(tabWidth.Value ?? 4);
        _editorView.Editor.IndentWidth = s.TabWidth;
        s.EditorPadding = (int)(padding.Value ?? 20);
        _editorView.Editor.HorizontalPadding = s.EditorPadding;
        s.EditorPaddingVertical = (int)(paddingY.Value ?? 14);
        _editorView.Editor.VerticalPadding = s.EditorPaddingVertical;
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

        s.CompletedTaskSort = (MarkdownEditor.CompletedTaskSortMode)System.Math.Clamp(completedTasks.SelectedIndex, 0, 2);
        _editorView.Editor.CompletedTaskSorting = s.CompletedTaskSort;

        SetViewMode((MarkdownEditor.ViewMode)System.Math.Clamp(viewMode.SelectedIndex, 0, 2)); // applies + syncs menu + saves

        _settings.Save();
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

    /// <summary>
    /// Coalesce the whole-document word/char scan to ~200ms after the last edit, so a burst of typing
    /// recomputes the counts once (when the user pauses) rather than on every keystroke.
    /// </summary>
    private void ScheduleDocumentStatus()
    {
        _statsTimer ??= CreateStatsTimer();
        _statsTimer.Stop();
        _statsTimer.Start();
    }

    private DispatcherTimer CreateStatsTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            UpdateDocumentStatus();
        };

        return timer;
    }

    /// <summary>Document-level status — the word/char counts scan the document, so this is debounced on typing.</summary>
    private void UpdateDocumentStatus()
    {
        _statsTimer?.Stop(); // a synchronous refresh supersedes any pending debounced one
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
