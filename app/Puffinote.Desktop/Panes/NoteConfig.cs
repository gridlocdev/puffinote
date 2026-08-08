using Puffinote.Components.Controls;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Shell;
using Puffinote.Desktop.Shell;
using Typage.Control.Controls;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// The note-editor settings Puffinote inherits from Typage's <em>own</em> config file (PRD §7.5), read into
/// a small local mirror so Puffinote never references Typage's desktop shell (one-way layering, PRD §9;
/// <see cref="EditorConfig"/> explains the pattern). A note opened in Puffinote therefore behaves as it does
/// in standalone Typage — same indent width, insets, view mode and toolbar — unless a Puffinote override
/// wins (<see cref="WithOverrides"/>).
/// <para>
/// The mirror covers what Puffinote can act on. Settings that only mean something to Typage's own window
/// are left out and left alone — they stay in the file, untouched by a save from here, and the Preferences
/// tab's "edit the file" button is how you reach them. That is its theme (Puffinote sets the family theme
/// itself), its crash-recovery snapshot interval, and its auto-save interval: saving an open document in
/// Puffinote is the <em>shell's</em> job, so a note in a tab is auto-saved on Puffinote's own interval
/// (General ▸ Saving) and Typage's has nothing to say about it.
/// </para>
/// </summary>
internal sealed record NoteConfig
{
    public int TabWidth { get; set; } = 4;

    public int EditorPadding { get; set; } = 20;

    public int EditorPaddingVertical { get; set; } = 14;

    /// <summary>The widest the reading column may grow, in pixels; 0 means the full pane width.</summary>
    public int EditorMaxContentWidth { get; set; } = 760;

    /// <summary>How much of Typage's formatting toolbar sits above the note: full, slim handle, or nothing.</summary>
    public CommandBarVisibility Toolbar { get; set; }

    /// <summary>The presentation the note opens in: rich, formatted-with-markers, or raw source.</summary>
    public MarkdownEditor.ViewMode View { get; set; } = MarkdownEditor.ViewMode.Rich;

    /// <summary>Whether ticking a task re-orders its sibling group.</summary>
    public MarkdownEditor.CompletedTaskSortMode CompletedTaskSort { get; set; } = MarkdownEditor.CompletedTaskSortMode.None;

    /// <summary>Typage's own settings file — the same path standalone Typage reads and writes.</summary>
    public static string DefaultPath => SettingsStore.AppConfigPath("Typage", "settings.json");

    /// <summary>Load Typage's own note settings (tolerant — absent/corrupt yields Typage's defaults).</summary>
    public static NoteConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        var config = SettingsStore.Load(path, static () => new NoteConfig(), EditorConfig.Json);

        // Mirror Typage's own migration of its pre-three-state boolean, so a file that build wrote reads the
        // same here as it does there — a mirror that skipped it would show a toolbar Typage doesn't.
        if (SettingsStore.MigratedBool(path, nameof(Toolbar), "ShowToolbar") is { } shown)
        {
            config.Toolbar = shown ? CommandBarVisibility.Visible : CommandBarVisibility.Hidden;
        }

        return config;
    }

    /// <summary>
    /// Write these settings back into Typage's own file, merging rather than replacing so the keys this
    /// mirror doesn't model — Typage's theme, its recent-files list — survive an edit made from Puffinote.
    /// </summary>
    public void Save(string? path = null) =>
        SettingsStore.Merge(path ?? DefaultPath, this, EditorConfig.Json);

    /// <summary>The Preferences form's "Notes" tab, over this instance (nothing is written until it commits).</summary>
    public IReadOnlyList<SettingsSection> Fields() =>
    [
        new SettingsSection("Appearance",
        [
            ChoiceSetting.ForEnum<MarkdownEditor.ViewMode>("Default view", () => View, v => View = v,
                ["Rich", "Formatted", "Raw text"]),
            ChoiceSetting.ForEnum<CommandBarVisibility>("Formatting toolbar", () => Toolbar, v => Toolbar = v,
                ["Visible", "Collapsed", "Hidden"],
                "Visible shows the full toolbar; Collapsed folds it to its slim handle; Hidden gives the whole row back to the text."),
        ]),
        new SettingsSection("Editor",
        [
            new NumberSetting("Tab width", () => TabWidth, v => TabWidth = (int)v, 1, 8),
            new NumberSetting("Horizontal padding", () => EditorPadding, v => EditorPadding = (int)v, 0, 200, step: 4,
                hint: "Pixels of inset on each side of the text."),
            new NumberSetting("Vertical padding", () => EditorPaddingVertical, v => EditorPaddingVertical = (int)v, 0, 200, step: 4,
                hint: "Pixels of inset above and below the text."),
            new NumberSetting("Reading width", () => EditorMaxContentWidth, v => EditorMaxContentWidth = (int)v, 0, 4000, step: 20,
                hint: "The widest the text column may grow, in pixels. 0 uses the full width."),
        ]),
        new SettingsSection("Editing",
        [
            ChoiceSetting.ForEnum<MarkdownEditor.CompletedTaskSortMode>("Completed tasks",
                () => CompletedTaskSort, v => CompletedTaskSort = v,
                ["Leave in place", "Move to top", "Move to bottom"]),
        ]),
    ];

    /// <summary>Layer Puffinote's overrides on top of these inherited settings (PRD §7.5 precedence).</summary>
    public NoteConfig WithOverrides(PuffinoteSettings overrides) =>
        this with { TabWidth = overrides.TabWidth ?? TabWidth };

    /// <summary>The resolved config for <paramref name="overrides"/>: Typage's own file, then the overrides.</summary>
    public static NoteConfig Resolve(PuffinoteSettings overrides, string? typagePath = null) =>
        Load(typagePath).WithOverrides(overrides);
}
