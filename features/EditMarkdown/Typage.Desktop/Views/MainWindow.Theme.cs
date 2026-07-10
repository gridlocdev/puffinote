using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Desktop.Services;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
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
        EditorView.EditorTheme = editorTheme;

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
        _toolbar?.IsVisible = show;

        _toggleToolbarItem?.IsChecked = show;

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
        EditorView.Editor.View = mode;
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
        // In focus mode the toolbar is hidden; otherwise restore the user's toolbar preference.
        _toolbar?.IsVisible = !_focusMode && _settings.Settings.ShowToolbar;

        _statusBar?.IsVisible = !_focusMode;

        _toggleFocusItem?.IsChecked = _focusMode;
    }
}
