using Puffinote.Components.Shell;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// The note-editor settings Puffinote inherits from Typage's <em>own</em> config file (PRD §7.5), read into
/// a small local mirror so Puffinote never references Typage's desktop shell (one-way layering, PRD §9). A
/// note opened in Puffinote therefore behaves as it does in standalone Typage — same indent width and text
/// insets — unless a Puffinote override wins (<see cref="WithOverrides"/>). Fields Puffinote doesn't apply
/// to the embeddable control (theme, recent files, autosave…) are simply ignored on load; the values here
/// mirror Typage's <c>AppSettings</c> defaults so an absent/partial file still reads as standalone Typage.
/// </summary>
internal sealed record NoteConfig
{
    public int TabWidth { get; init; } = 4;

    public int EditorPadding { get; init; } = 20;

    public int EditorPaddingVertical { get; init; } = 14;

    /// <summary>Typage's own settings file — the same path standalone Typage reads and writes.</summary>
    private static string TypagePath => SettingsStore.AppConfigPath("Typage", "settings.json");

    /// <summary>Load Typage's own note settings (tolerant — absent/corrupt yields Typage's defaults).</summary>
    public static NoteConfig Load(string? path = null) =>
        SettingsStore.Load(path ?? TypagePath, static () => new NoteConfig());

    /// <summary>Layer Puffinote's overrides on top of these inherited settings (PRD §7.5 precedence).</summary>
    public NoteConfig WithOverrides(PuffinoteSettings overrides) =>
        this with { TabWidth = overrides.TabWidth ?? TabWidth };

    /// <summary>The resolved config for <paramref name="overrides"/>: Typage's own file, then the overrides.</summary>
    public static NoteConfig Resolve(PuffinoteSettings overrides, string? typagePath = null) =>
        Load(typagePath).WithOverrides(overrides);
}
