using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Typage.Control.Views;
using Typage.Desktop.Services;

namespace Typage.Desktop.Views;

/// <summary>
/// The standalone shell's single window (PRD §5 — one window, one document, one process). A thin
/// host around the embeddable <see cref="MarkdownEditorView"/>: it owns the menu, the open/save file
/// dialogs, recent files, the dirty-state guard, the status bar, and settings. All editing logic — and the
/// formatting toolbar, find bar, and link/image/reference dialogs with it — lives in the control and engine,
/// so this shell and the Puffinote shell present the same editor rather than two that drift.
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
    private readonly RecoveryService _recovery;
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

    // Chrome brushes are mutated live on a theme change; the status bar, and the editor's command bar
    // through the Chrome* resources, share these instances. Colours are the family's (see ApplyChromeColors).
    private readonly SolidColorBrush _chromeBg = new(Color.FromRgb(0x25, 0x25, 0x26));
    private readonly SolidColorBrush _chromeBorder = new(Color.FromRgb(0x3A, 0x3A, 0x3A));
    private readonly SolidColorBrush _mutedText = new(Color.FromRgb(0x9A, 0x9E, 0xA6));

    private static readonly FilePickerFileType MarkdownType = new("Markdown")
    {
        Patterns = ["*.md", "*.markdown", "*.mdown", "*.txt"],
    };

    public MainWindow(SettingsService settings)
        : this(settings, new RecoveryService())
    {
    }

    /// <summary>
    /// Test seam: point crash recovery at a scratch directory instead of the user's profile. A snapshot
    /// left there by any unclean shutdown — the app's own, ever — otherwise greets every headless window
    /// with the "Recover unsaved changes?" modal, which disables every command and traps Tab. Tests that
    /// never mention recovery then fail on a developer's machine and pass on fresh CI.
    /// </summary>
    internal MainWindow(SettingsService settings, RecoveryService recovery)
    {
        _settings = settings;
        _recovery = recovery;
        Width = 900;
        Height = 680;
        Title = "Typage";

        // The editor's formatting toolbar is the family's shared AdaptiveCommandBar, and it paints its chrome
        // from the host's ChromeBackground/ChromeBorder resources — Puffinote's App supplies them, and without
        // them here the bar drew with no background at all (the window's near-black Fluent surface showed
        // through). Publish the very brush instances the status bar uses, so the shell's two chrome strips are
        // one palette and ApplyChromeColors re-tints both at once.
        Resources["ChromeBackground"] = _chromeBg;
        Resources["ChromeBorder"] = _chromeBorder;

        var root = new DockPanel();
        InstallMenu(root); // macOS native menu bar, or the in-window menu elsewhere; plus key bindings

        // The formatting toolbar and the find bar are the editor's own, docked inside it — this shell just
        // says whether the toolbar shows. It used to ship a second, hand-rolled bar of Unicode glyphs beside
        // the control's; there is now one icon-driven bar, and Puffinote renders the very same one.
        EditorView.ShowCommandBar = _settings.Settings.ShowToolbar;
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

        // The view-mode commands now live in the editor's catalog, so the switch happens inside the control.
        // The shell still owns what a shell owns: persisting the choice and syncing the View ▸ radio group.
        EditorView.ViewModeChanged += (_, _) => OnViewModeChanged();
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
        EditorView.ViewMode = _settings.Settings.View; // restore the view-mode default
        EditorView.Editor.CompletedTaskSorting = _settings.Settings.CompletedTaskSort; // restore task-sort default
        EditorView.Editor.HorizontalPadding = _settings.Settings.EditorPadding; // restore editor padding
        EditorView.Editor.MaxContentWidth = _settings.Settings.EditorMaxContentWidth; // and the reading-column cap
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

    internal bool ToolbarVisibleForTest => EditorView.ShowCommandBar;

    internal void ToggleFocusModeForTest() => ToggleFocusMode();

    internal void SetViewModeForTest(MarkdownEditor.ViewMode mode) => EditorView.ViewMode = mode;
}
