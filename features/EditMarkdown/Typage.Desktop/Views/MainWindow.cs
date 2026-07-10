using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Typage.Control.Controls;
using Typage.Control.Views;
using Typage.Desktop.Services;
using Typage.Engine.Document;

namespace Typage.Desktop.Views;

/// <summary>
/// The standalone shell's single window (PRD §5 — one window, one document, one process). A thin
/// host around the embeddable <see cref="MarkdownEditorView"/>: it owns the menu, file dialogs,
/// recent files, the dirty-state guard, the status bar, and HTML export. All editing logic lives
/// in the control and engine; this layer is replaceable by the future notes-app shell.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
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
    private Dictionary<string, AppCommand> _commandsById = [];
    private readonly FindBar? _findBar;
    private readonly Border? _toolbar;
    private readonly Border? _statusBar;
    private CheckItem? _toggleToolbarItem;
    private CheckItem? _toggleFocusItem;
    private readonly List<(MarkdownEditor.ViewMode Mode, CheckItem Item)> _viewModeItems = [];
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
    private bool _recoveryChecked;
    private readonly List<(AppTheme Theme, CheckItem Item)> _themeItems = [];

    // Unsaved-changes guard: set once the user has confirmed a close so OnClosing lets it through.
    private bool _closingConfirmed;

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
                _menu?.IsChecked = value;

                _native?.IsChecked = value;
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
        Patterns = ["*.md", "*.markdown", "*.mdown", "*.txt"],
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
        _findBar = new FindBar(EditorView, _chromeBg, _chromeBorder);
        DockPanel.SetDock(_findBar, Dock.Top);
        root.Children.Add(_findBar);
        _statusBar = BuildStatusBar();
        root.Children.Add(_statusBar);
        root.Children.Add(EditorView);

        // The command palette and the modal overlay host float over everything; the host sits on top so
        // a dialog renders above the palette. When a modal closes, return focus to the editor.
        Content = new Panel { Children = { root, _palette, _overlayHost } };
        _overlayHost.Closed += (_, _) => EditorView.Focus();

        EditorView.TextChanged += (_, _) =>
        {
            // The title + caret status are cheap; refresh them now. The word/char counts scan the whole
            // document, so debounce them — recompute once typing pauses instead of on every keystroke.
            UpdateTitle();
            UpdateCaretStatus();
            ScheduleDocumentStatus();
        };
        EditorView.SelectionChanged += (_, _) => UpdateCaretStatus();
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
        EditorView.Editor.IndentWidth = _settings.Settings.TabWidth;
        EditorView.Editor.View = _settings.Settings.View; // restore the view-mode default
        EditorView.Editor.CompletedTaskSorting = _settings.Settings.CompletedTaskSort; // restore task-sort default
        EditorView.Editor.HorizontalPadding = _settings.Settings.EditorPadding; // restore editor padding
        EditorView.Editor.VerticalPadding = _settings.Settings.EditorPaddingVertical;

        SetupAutosave();
        Opened += async (_, _) => await CheckForRecoveryAsync(); // offer recovery once the window is up

        RefreshRecentMenu();
        UpdateChrome();
    }

    /// <summary>The embedded editor view, exposed for the headless UI tests.</summary>
    internal MarkdownEditorView EditorView { get; } = new();

    // Test hooks for the view toggles (the chrome controls are private).
    internal bool StatusBarVisibleForTest => _statusBar?.IsVisible ?? false;

    internal bool ToolbarVisibleForTest => _toolbar?.IsVisible ?? false;

    internal void ToggleFocusModeForTest() => ToggleFocusMode();

    internal void SetViewModeForTest(MarkdownEditor.ViewMode mode) => SetViewMode(mode);
}
