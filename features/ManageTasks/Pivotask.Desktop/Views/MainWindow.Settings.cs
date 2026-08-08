using Pivotask.Control.Views;
using Pivotask.Core.Query;
using Pivotask.Desktop.Services;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;

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
        new SettingsSection("Board",
        [
            ChoiceSetting.ForEnum<BoardLayout>("Open in", () => s.Layout, v => s.Layout = v,
                ["Board", "List"],
                "Columns or grouped rows — the same tasks, read two ways."),
            ChoiceSetting.ForEnum<TaskFilter>("Show", () => s.Filter, v => s.Filter = v,
                ["All tasks", "Starred", "Due soon", "Unfinished"],
                "The filter a board opens with. The View menu still switches it per session."),
        ]),
    ]);

    /// <summary>Push the committed settings into the live board.</summary>
    private void ApplySettings(AppSettings s)
    {
        BoardView.Layout = s.Layout;
        BoardView.Filter = s.Filter;
    }
}
