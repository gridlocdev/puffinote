using Avalonia;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Pivotask.Desktop.Services;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;

namespace Pivotask.Desktop.Views;

public sealed partial class MainWindow
{
    /// <summary>
    /// The Preferences dialog — every option in Pivotask's <c>settings.json</c>. The form is the shared
    /// <see cref="SettingsEditorView"/>, so this reads the same as every other app in the family; what
    /// stays here is which settings exist and what applying one means for the live board.
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
        new SettingsSection("Board",
        [
            ChoiceSetting.ForEnum<BoardLayout>("Open in", () => s.Layout, v => s.Layout = v,
                ["Board", "List"],
                "Columns or grouped rows — the same tasks, read two ways."),
            ChoiceSetting.ForEnum<TaskFilter>("Show", () => s.Filter, v => s.Filter = v,
                ["All tasks", "Unfinished", "Starred", "Due tomorrow", "Due this week"],
                "The filter a board opens with. The View menu still switches it per session."),
        ]),
        new SettingsSection("Saving",
        [
            AutoSave.IntervalField(() => s.AutoSaveSeconds, v => s.AutoSaveSeconds = v),
        ]),
    ]);

    /// <summary>Push the committed settings into the live board.</summary>
    private void ApplySettings(AppSettings s)
    {
        if (Application.Current is { } app)
        {
            // Requesting the variant re-resolves Fluent's control themes and re-tints the family's chrome
            // tokens in one go, and the board paints from those tokens — so the cards, columns and dialogs
            // follow without this having to walk them.
            s.Theme.Apply(app);
        }

        BoardView.Layout = s.Layout;
        BoardView.Filter = s.Filter;
        _autoSave.IntervalSeconds = s.AutoSaveSeconds; // takes effect on the board already open
    }
}
