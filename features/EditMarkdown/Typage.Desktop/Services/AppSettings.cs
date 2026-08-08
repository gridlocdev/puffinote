using System.Text.Json.Serialization;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;
using Typage.Control.Controls;
using RecentFilesHelper = Puffinote.Components.Shell.RecentFiles;

namespace Typage.Desktop.Services;

/// <summary>
/// The appearance mode (PRD §7.2) — the family's three choices, plus one of Typage's own.
/// <see cref="System"/> follows the OS light/dark setting.
/// <para>
/// <see cref="HighContrast"/> is not a fourth colour scheme so much as a stronger dark one: it pairs the
/// dark shell with an editor palette built for maximum separation. It stays Typage-only, and stays in this
/// enum rather than in the shared <see cref="ThemePreference"/>, because no other app in the family has a
/// document palette to render it in — offering it where it changed nothing but the drop-down would be a
/// promise the app couldn't keep.
/// </para>
/// </summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
    HighContrast,
}

/// <summary>Maps Typage's appearance modes onto the family's shared three.</summary>
public static class AppThemeExtensions
{
    /// <summary>
    /// The shared preference this mode is an instance of. High contrast resolves to
    /// <see cref="ThemePreference.Dark"/>: it is the variant its chrome is drawn in, and what the editor
    /// then does with it is the part that makes it high contrast.
    /// </summary>
    public static ThemePreference ToPreference(this AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemePreference.Light,
        AppTheme.Dark or AppTheme.HighContrast => ThemePreference.Dark,
        _ => ThemePreference.System,
    };
}

/// <summary>
/// User preferences persisted as JSON in the OS app-config location (PRD §7.9). A small subset
/// for v1: theme, recent files, tab width, and how much of the formatting toolbar shows.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// The appearance mode. Unset means <see cref="AppTheme.System"/>: an app that has never been told
    /// which appearance to wear should wear the one the rest of the desktop is wearing, not the one its
    /// authors happened to prefer.
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    public List<string> RecentFiles { get; set; } = [];

    public int TabWidth { get; set; } = 4;

    /// <summary>The left/right text inset (padding) of the editor, in pixels, applied to both sides (PRD §7.5).</summary>
    public int EditorPadding { get; set; } = 20;

    /// <summary>The top/bottom text inset (padding) of the editor, in pixels, applied to both ends (PRD §7.5).</summary>
    public int EditorPaddingVertical { get; set; } = 14;

    /// <summary>
    /// The widest the reading column may grow, in pixels; <c>0</c> means full width. Prose much beyond ~45–90
    /// characters a line is harder to read, so the column is capped and centred by default — but full width is
    /// a legitimate preference for a Markdown author, so it stays a choice.
    /// </summary>
    public int EditorMaxContentWidth { get; set; } = 760;

    /// <summary>
    /// How much of the formatting toolbar a window opens with — the full bar, its slim collapse handle, or
    /// nothing at all (PRD §7.5). Default: the full bar.
    /// </summary>
    public CommandBarVisibility Toolbar { get; set; }

    /// <summary>
    /// The reading zoom, on the family's shared ladder (1.0 = 100%). Remembered because someone who needs
    /// larger text needs it every session, not once.
    /// </summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// The default presentation mode for the editing surface: the WYSIWYG rich view, the formatted view
    /// with markers revealed, or the plain raw-text source (PRD §7.4).
    /// </summary>
    public MarkdownEditor.ViewMode View { get; set; } = MarkdownEditor.ViewMode.Rich;

    /// <summary>
    /// Seconds of no editing before the document is written back to its own file; 0 turns auto-save off.
    /// Distinct from <see cref="RecoverySnapshotSeconds"/>: this saves the <i>document</i>, that snapshots
    /// unsaved work to a sidecar. Both exist because they cover different holes — a document that was never
    /// saved has no path to auto-save to, and is exactly the one worth recovering.
    /// </summary>
    public int AutoSaveSeconds { get; set; } = AutoSave.DefaultSeconds;

    /// <summary>Seconds between crash-recovery snapshots; 0 disables them (PRD §9.4).</summary>
    public int RecoverySnapshotSeconds { get; set; } = 30;

    /// <summary>
    /// What <see cref="RecoverySnapshotSeconds"/> was called before document auto-save existed and took the
    /// name. Read so an existing settings file keeps the interval its owner chose; never written back — the
    /// getter is always null, and a null is omitted from the output — so the key retires on the next save
    /// rather than living on beside a similarly-spelled one that means something else.
    /// </summary>
    [JsonPropertyName("AutosaveSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyAutosaveSeconds
    {
        get => null;
        set
        {
            if (value is { } seconds)
            {
                RecoverySnapshotSeconds = seconds;
            }
        }
    }

    /// <summary>
    /// How a checkbox list re-orders itself when a task is toggled: leave items in place
    /// (<see cref="MarkdownEditor.CompletedTaskSortMode.None"/>, the default) or float completed
    /// items to the top/bottom of their sibling group (PRD §7.2 editing assists).
    /// </summary>
    public MarkdownEditor.CompletedTaskSortMode CompletedTaskSort { get; set; } = MarkdownEditor.CompletedTaskSortMode.None;

    public const int MaxRecentFiles = 10;

    public void PushRecent(string path) =>
        RecentFiles = RecentFilesHelper.Push(RecentFiles, path, MaxRecentFiles);
}
