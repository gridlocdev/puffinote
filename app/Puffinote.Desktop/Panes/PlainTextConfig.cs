using Puffinote.Components.Controls;
using Puffinote.Components.Shell;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// The plaintext settings Puffinote inherits from Plaintain's <em>own</em> config file — a mirror, for the
/// reasons <see cref="EditorConfig"/> gives. Plaintain's file is short by design, and this covers all of its
/// preferences; what it leaves out is the recent-files list, which is a record of what you did rather than
/// something to set.
/// </summary>
internal sealed record PlainTextConfig
{
    /// <summary>Whether long lines fold. Off is the Notepad default.</summary>
    public bool WordWrap { get; set; }

    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Whether the caret's row is washed with a faint tint. On, as Plaintain draws it.</summary>
    public bool HighlightCurrentLine { get; set; } = true;

    public double FontSize { get; set; } = 13;

    /// <summary>Plaintain's own settings file — the same path standalone Plaintain reads and writes.</summary>
    public static string DefaultPath => SettingsStore.AppConfigPath("Plaintain", "settings.json");

    /// <summary>Load Plaintain's own settings (tolerant — absent/corrupt yields Plaintain's defaults).</summary>
    public static PlainTextConfig Load(string? path = null) =>
        SettingsStore.Load(path ?? DefaultPath, static () => new PlainTextConfig(), EditorConfig.Json);

    /// <summary>Write these back into Plaintain's own file, merging so the recent-files list survives.</summary>
    public void Save(string? path = null) =>
        SettingsStore.Merge(path ?? DefaultPath, this, EditorConfig.Json);

    /// <summary>The Preferences form's "Plain Text" tab, over this instance.</summary>
    public IReadOnlyList<SettingsSection> Fields() =>
    [
        new SettingsSection("Editor",
        [
            new ToggleSetting("Wrap long lines", () => WordWrap, v => WordWrap = v,
                "Off is the Notepad default: a long line runs off the right edge instead of folding."),
            new ToggleSetting("Show line numbers", () => ShowLineNumbers, v => ShowLineNumbers = v),
            new ToggleSetting("Highlight the current line", () => HighlightCurrentLine, v => HighlightCurrentLine = v,
                "A faint tint across the row the caret is on. On a log or an aligned table it lands on the data rather than on the caret, so it can be turned off; the gutter still marks the line either way."),
            new NumberSetting("Font size", () => FontSize, v => FontSize = v, 8, 48, step: 1, decimals: 1,
                hint: "Point size of the editor text."),
        ]),
    ];
}
