using Avalonia;
using Plaintain.Desktop.Services;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;

namespace Plaintain.Desktop.Views;

public sealed partial class MainWindow
{
    /// <summary>
    /// The Preferences dialog — every option in Plaintain's <c>settings.json</c> that is a preference
    /// rather than a record of what you did (the recent-files list is the latter, and File ▸ Open Recent
    /// is where it belongs). The form is the shared <see cref="SettingsEditorView"/>, so this reads the
    /// same as every other app in the family; what stays here is which settings exist and what applying
    /// one means for the live editor.
    /// </summary>
    private async void ShowSettings()
    {
        var s = _settings.Settings;
        var form = BuildSettingsForm(s);

        if (!await _overlay.ShowModalAsync(new FormDialog("Preferences", form)))
        {
            return; // Cancel / Esc — leave settings untouched
        }

        form.Commit();
        ApplySettings(s);
        _settings.Save();
    }

    /// <summary>The Preferences form over <paramref name="s"/> (also the seam the UI tests drive).</summary>
    internal static SettingsEditorView BuildSettingsForm(AppSettings s) => new(
    [
        new SettingsSection("Appearance",
        [
            ChoiceSetting.ForEnum<ThemePreference>("Theme", () => s.Theme, v => s.Theme = v,
                ThemePreferences.Labels, ThemePreferences.Hint),
        ]),
        new SettingsSection("Editor",
        [
            new ToggleSetting("Wrap long lines at words", () => s.WordWrap, v => s.WordWrap = v,
                "Off is the Notepad default: a long line runs off the right edge instead of folding. On, it folds at the window's edge between words, so only a word too long for a whole row is ever split."),
            new ToggleSetting("Show line numbers", () => s.ShowLineNumbers, v => s.ShowLineNumbers = v),
            new ToggleSetting("Highlight the current line", () => s.HighlightCurrentLine, v => s.HighlightCurrentLine = v,
                "A faint tint across the row the caret is on. On a log or an aligned table it lands on the data rather than on the caret, so it can be turned off; the gutter still marks the line either way."),
            new NumberSetting("Font size", () => s.FontSize, v => s.FontSize = v, 8, 48, step: 1, decimals: 1,
                hint: "Point size of the editor text."),
        ]),
        new SettingsSection("Saving",
        [
            AutoSave.IntervalField(() => s.AutoSaveSeconds, v => s.AutoSaveSeconds = v),
        ]),
    ]);

    /// <summary>Push the committed settings into the live editor.</summary>
    private void ApplySettings(AppSettings s)
    {
        ApplyTheme(s.Theme);

        // WordWrap and ShowLineNumbers persist themselves through the editor's PropertyChanged hook
        // (see the constructor); assigning them here is what makes the change visible.
        EditorView.Editor.WordWrap = s.WordWrap;
        EditorView.Editor.ShowLineNumbers = s.ShowLineNumbers;
        EditorView.Editor.HighlightCurrentLine = s.HighlightCurrentLine;
        EditorView.Editor.FontSize = s.FontSize;
        _autoSave.IntervalSeconds = s.AutoSaveSeconds; // takes effect on the document already open
    }

    /// <summary>
    /// Wear <paramref name="theme"/>: request the variant, which re-resolves Fluent's control themes and
    /// the family's chrome tokens together, and repaint the editing surface, which draws itself and so
    /// isn't reached by either. Called as the window opens and again whenever Preferences commits, so the
    /// choice lands on the window in front of you rather than on the next one you open.
    /// </summary>
    private void ApplyTheme(ThemePreference theme)
    {
        if (Application.Current is { } app)
        {
            theme.Apply(app);
        }

        EditorView.Editor.InvalidateVisual();
    }
}
