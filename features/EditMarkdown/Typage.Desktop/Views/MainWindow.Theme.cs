using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Theming;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Desktop.Services;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    private static string ThemeLabel(AppTheme theme) => theme switch
    {
        AppTheme.HighContrast => "High Contrast",
        _ => ThemePreferences.Label(theme.ToPreference()),
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
    /// <para>
    /// The variant goes through the shared <see cref="ThemePreferences.Apply"/>, which is also what puts
    /// <see cref="Tokens"/> into the new variant — so the family chrome this window borrows (the command
    /// palette, the modal dialogs, the title bar) changes with it rather than staying whatever it was
    /// built in. What stays Typage's own is the editor palette below, which is a document's typography
    /// and not chrome, and the high-contrast mode, which nothing else in the family offers.
    /// </para>
    /// </summary>
    private void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var variant = theme.ToPreference().Apply(app);

        var editorTheme = theme switch
        {
            AppTheme.Light => MarkdownTheme.Light,
            AppTheme.HighContrast => MarkdownTheme.HighContrast,
            AppTheme.Dark => MarkdownTheme.Dark,
            _ => variant == ThemeVariant.Light ? MarkdownTheme.Light : MarkdownTheme.Dark,
        };
        EditorView.EditorTheme = editorTheme;

        var isLight = ReferenceEquals(editorTheme, MarkdownTheme.Light);
        ApplyChromeColors(isLight, theme == AppTheme.HighContrast);

        foreach (var (t, item) in _themeItems)
        {
            item.IsChecked = t == theme; // keep the View ▸ Theme radios in sync
        }
    }

    /// <summary>
    /// Re-tint the shell's chrome brushes — the status bar's, and (through the window's <c>ChromeBackground</c>
    /// / <c>ChromeBorder</c> resources) the editor's command bar. Light and dark are the family's chrome
    /// palette, the same values Puffinote and Cellestra give their hosted editors, so the same bar is the same
    /// colour in whichever shell you meet it. High contrast is this shell's alone: nothing embeds it.
    /// </summary>
    private void ApplyChromeColors(bool isLight, bool isHighContrast)
    {
        if (isHighContrast)
        {
            _chromeBg.Color = Color.FromRgb(0x0A, 0x0A, 0x0A);
            _chromeBorder.Color = Color.FromRgb(0x66, 0x66, 0x66);
            _mutedText.Color = Color.FromRgb(0xDD, 0xDD, 0xDD);
        }
        else if (isLight)
        {
            _chromeBg.Color = Color.FromRgb(0xFA, 0xFA, 0xFA);
            _chromeBorder.Color = Color.FromRgb(0xE0, 0xE0, 0xE0);
            _mutedText.Color = Color.FromRgb(0x6A, 0x6E, 0x76);
        }
        else
        {
            _chromeBg.Color = Color.FromRgb(0x25, 0x25, 0x26);
            _chromeBorder.Color = Color.FromRgb(0x3A, 0x3A, 0x3A);
            _mutedText.Color = Color.FromRgb(0x9A, 0x9E, 0xA6);
        }
    }

    /// <summary>
    /// The menu/command label for a command-bar state (also used for the Preferences default; PRD §7.5).
    /// <para>
    /// "Command Bar", not "Toolbar": it is the same strip in the same place doing the same job as Cellestra's,
    /// which has always been called that, and one element with two names in two apps of one family is one
    /// element the reader has to learn twice. See the vocabulary note in the repo's AGENTS.md.
    /// </para>
    /// </summary>
    private static string ToolbarLabel(CommandBarVisibility state) => state switch
    {
        CommandBarVisibility.Visible => "Show Command Bar",
        CommandBarVisibility.Collapsed => "Collapse Command Bar",
        _ => "Hide Command Bar",
    };

    /// <summary>
    /// Put the formatting toolbar in <paramref name="state"/> and re-check the View ▸ radio group. A
    /// <paramref name="persist"/>ed change is a preference — the menu, the palette, Preferences, and the bar's
    /// own chevron; the unpersisted ones are the shell borrowing the row (startup restore, focus mode), which
    /// must not overwrite what the reader chose.
    /// </summary>
    private void ApplyToolbar(CommandBarVisibility state, bool persist)
    {
        _applyingToolbarState = !persist;
        try
        {
            EditorView.CommandBarVisibility = state;
        }
        finally
        {
            _applyingToolbarState = false;
        }

        if (persist)
        {
            _settings.Settings.CommandBar = state;
            _settings.Save();
        }

        SyncToolbarMenu(state);
    }

    /// <summary>
    /// The toolbar moved from the surface itself — the bar's collapse or expand chevron. That is the reader
    /// choosing, so it persists exactly like the menu does (unless the shell was the one moving it).
    /// </summary>
    private void OnToolbarVisibilityChanged()
    {
        if (_applyingToolbarState || _focusMode)
        {
            return; // our own restore, or focus mode's session-only hide — neither is a new preference
        }

        _settings.Settings.CommandBar = EditorView.CommandBarVisibility;
        _settings.Save();
        SyncToolbarMenu(EditorView.CommandBarVisibility);
    }

    private void SyncToolbarMenu(CommandBarVisibility state)
    {
        foreach (var (s, item) in _toolbarItems)
        {
            item.IsChecked = s == state;
        }
    }

    /// <summary>The menu/command label for a view mode (also used for the Preferences default; PRD §7.4).</summary>
    private static string ViewModeLabel(MarkdownEditor.ViewMode mode) => mode switch
    {
        MarkdownEditor.ViewMode.Rich => "View Rich View",
        MarkdownEditor.ViewMode.Formatted => "View Formatted View",
        _ => "View Raw Text",
    };

    /// <summary>
    /// The surface switched view mode (from the catalog command, the toolbar, or the palette). The shell's
    /// job is what follows: make the new mode the persisted default and re-check the View ▸ radio (PRD §7.4).
    /// </summary>
    private void OnViewModeChanged()
    {
        var mode = EditorView.ViewMode;
        _settings.Settings.View = mode;
        _settings.Save();
        foreach (var (m, item) in _viewModeItems)
        {
            item.IsChecked = m == mode;
        }
    }

    /// <summary>The reader zoomed. Remember it, and say so in the status bar's zoom readout.</summary>
    private void OnZoomChanged()
    {
        _settings.Settings.Zoom = EditorView.Zoom;
        _settings.Save();
        UpdateChrome();
    }

    /// <summary>Toggle distraction-free mode: hide the toolbar and status bar (session-only).</summary>
    private void ToggleFocusMode()
    {
        _focusMode = !_focusMode;
        // In focus mode the toolbar is hidden; otherwise restore the user's toolbar preference — neither
        // half of which is itself a preference, so the borrowed row is never written back over their choice.
        ApplyToolbar(_focusMode ? CommandBarVisibility.Hidden : _settings.Settings.CommandBar, persist: false);

        _statusBar?.IsVisible = !_focusMode;

        _toggleFocusItem?.IsChecked = _focusMode;
    }
}
