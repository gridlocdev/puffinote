using Plaintain.Desktop.Services;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;

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
        new SettingsSection("Editor",
        [
            new ToggleSetting("Wrap long lines", () => s.WordWrap, v => s.WordWrap = v,
                "Off is the Notepad default: a long line runs off the right edge instead of folding."),
            new ToggleSetting("Show line numbers", () => s.ShowLineNumbers, v => s.ShowLineNumbers = v),
            new NumberSetting("Font size", () => s.FontSize, v => s.FontSize = v, 8, 48, step: 1, decimals: 1,
                hint: "Point size of the editor text."),
        ]),
    ]);

    /// <summary>Push the committed settings into the live editor.</summary>
    private void ApplySettings(AppSettings s)
    {
        // WordWrap and ShowLineNumbers persist themselves through the editor's PropertyChanged hook
        // (see the constructor); assigning them here is what makes the change visible.
        EditorView.Editor.WordWrap = s.WordWrap;
        EditorView.Editor.ShowLineNumbers = s.ShowLineNumbers;
        EditorView.Editor.FontSize = s.FontSize;
    }
}
