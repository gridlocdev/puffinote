using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Views;

public partial class MainWindow
{
    /// <summary>
    /// The Preferences dialog over Puffinote's <b>own</b> config file (PRD §7.5) — the per-user override
    /// layer, not the editors' settings. That is what makes an override an override here: each option can
    /// be left off, in which case a note behaves exactly as it does in standalone Typage, and turning it on
    /// is a deliberate act. The per-notebook <c>.puffinote/settings.json</c> still wins over anything set
    /// here, so a notebook that ships its own tab width keeps it; it is edited as a file, because that is
    /// the point of it being committable.
    /// <para>
    /// Session state — the last notebook, the sidebar width, the per-notebook recent list — is not a
    /// preference and isn't here; it's recorded as you work.
    /// </para>
    /// </summary>
    private async void ShowSettings()
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        // Edited as loose values rather than on the record: PuffinoteSettings is immutable, and "override
        // or inherit" is two controls over one nullable field, which only reads back as a pair.
        var overrideTabWidth = _settings.TabWidth is not null;
        double tabWidth = _settings.TabWidth ?? new NoteConfig().TabWidth;

        var form = new SettingsEditorView(
        [
            new SettingsSection("Notes",
            [
                new ToggleSetting("Override the note editor's tab width",
                    () => overrideTabWidth, v => overrideTabWidth = v,
                    "Off inherits Typage's own setting, so a note indents here as it does there."),
                new NumberSetting("Tab width", () => tabWidth, v => tabWidth = v, 1, 8),
            ]),
        ]);

        if (!await _overlayHost.ShowModalAsync(new FormDialog("Preferences", form)))
        {
            return; // Cancel / Esc — leave settings untouched
        }

        form.Commit();
        _settings = _settings with { TabWidth = overrideTabWidth ? (int)tabWidth : null };
        _settings.SaveUser();

        // Re-resolve against the open notebook's own file and push the result at the tab host, so open
        // panes pick the change up without a restart (PRD §7.5 precedence, applied live).
        _tabHost.Settings = PuffinoteSettings.Resolve(_settings, _notebook.RootPath);
    }
}
