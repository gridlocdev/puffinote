using Puffinote.Components.Controls;
using Puffinote.Components.Shell;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// The spreadsheet settings Puffinote inherits from Cellestra's <em>own</em> config file — a mirror, for the
/// reasons <see cref="EditorConfig"/> gives — so a sheet opened here starts the way it does in standalone
/// Cellestra.
/// <para>
/// Deliberately narrow. Cellestra's file also holds its window size and recent files (session state, not
/// preferences) and the radial menu's per-segment bindings, which have a dedicated editor of their own
/// inside the spreadsheet view — and which a merge-write must never model half of, since replacing that
/// object wholesale would throw away every binding the user made. All of it stays in the file, untouched by
/// a save from here.
/// </para>
/// </summary>
internal sealed record SheetConfig
{
    /// <summary>Whether the adaptive command bar starts folded to its slim handle.</summary>
    public bool CommandBarCollapsed { get; set; }

    /// <summary>The zoom a sheet opens at (1.0 = 100%).</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// Cellestra's own settings file. Note the path: Cellestra writes to the macOS application-support
    /// directory rather than the XDG-style config directory the other three use, and this mirrors that
    /// exactly — reading a different file would be reading nobody's settings.
    /// </summary>
    public static string DefaultPath
    {
        get
        {
            string home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return System.IO.Path.Combine(home, "Library", "Application Support", "Cellestra", "settings.json");
        }
    }

    /// <summary>Load Cellestra's own settings (tolerant — absent/corrupt yields Cellestra's defaults).</summary>
    public static SheetConfig Load(string? path = null) =>
        SettingsStore.Load(path ?? DefaultPath, static () => new SheetConfig(), EditorConfig.Json);

    /// <summary>Write these back into Cellestra's own file, merging so everything unmodelled survives.</summary>
    public void Save(string? path = null) =>
        SettingsStore.Merge(path ?? DefaultPath, this, EditorConfig.Json);

    /// <summary>The Preferences form's "Spreadsheets" tab, over this instance.</summary>
    public IReadOnlyList<SettingsSection> Fields() =>
    [
        new SettingsSection("View",
        [
            new NumberSetting("Zoom", () => Zoom * 100, v => Zoom = v / 100, 25, 400, step: 10,
                hint: "Percent. ⌘+ and ⌘− change it while you work; this is where a sheet starts."),
            new ToggleSetting("Collapse the command bar", () => CommandBarCollapsed, v => CommandBarCollapsed = v,
                "Fold the toolbar to its slim handle, for more grid."),
        ]),
    ];
}
