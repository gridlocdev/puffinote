using System.Text.Json;
using Puffinote.Components.Shell;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// Puffinote's own shell-level configuration — the <b>override</b> layer that sits on top of each embedded
/// editor's own settings (PRD §7.5 precedence: sub-app defaults &lt; sub-app config file &lt; Puffinote
/// overrides). A null field means "don't override — inherit whatever the note/sheet editor already uses";
/// a set field wins. Stored at two scopes (PRD §7.1): a per-user file (global preferences) and an optional
/// per-notebook <c>.puffinote/settings.json</c> that is durable/committable so overrides travel with the repo.
/// The per-notebook file wins where both set a value.
/// </summary>
internal sealed record PuffinoteSettings
{
    /// <summary>Overrides the note editor's indent (tab) width, in spaces; null inherits Typage's own.</summary>
    public int? TabWidth { get; init; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Puffinote's own per-user config file — the one the General tab of Preferences writes.</summary>
    public static string UserPath => SettingsStore.AppConfigPath("Puffinote", "settings.json");

    private static string NotebookPath(string root) =>
        System.IO.Path.Combine(root, NotebookNode.StateDirectoryName, "settings.json");

    /// <summary>Load the per-user Puffinote settings (tolerant — absent/corrupt yields defaults).</summary>
    public static PuffinoteSettings LoadUser() =>
        SettingsStore.Load(UserPath, static () => new PuffinoteSettings(), Options);

    /// <summary>Persist the per-user Puffinote settings (best-effort).</summary>
    public void SaveUser() => SettingsStore.Save(UserPath, this, Options);

    /// <summary>
    /// The effective overrides for <paramref name="notebookRoot"/>: the per-user <paramref name="user"/>
    /// settings with the notebook's own <c>.puffinote/settings.json</c> layered on top (the notebook wins where it
    /// sets a value). A notebook with no settings file yields the user settings unchanged.
    /// </summary>
    public static PuffinoteSettings Resolve(PuffinoteSettings user, string? notebookRoot)
    {
        if (notebookRoot is null)
        {
            return user;
        }

        var notebook = SettingsStore.Load(NotebookPath(notebookRoot), static () => new PuffinoteSettings(), Options);
        return user.Layer(notebook);
    }

    /// <summary>Return a copy with every set field of <paramref name="over"/> replacing this one's.</summary>
    public PuffinoteSettings Layer(PuffinoteSettings over) =>
        new() { TabWidth = over.TabWidth ?? TabWidth };
}
