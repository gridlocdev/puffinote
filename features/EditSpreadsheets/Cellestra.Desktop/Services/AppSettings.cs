using Cellestra.Desktop.Commands;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;
using RecentFilesHelper = Puffinote.Components.Shell.RecentFiles;

namespace Cellestra.Desktop.Services;

/// <summary>
/// User settings persisted between sessions (ARCHITECTURE §8): command-bar/zoom/view state, window
/// size, the recent-files list, and the radial-menu bindings. Stored as JSON under the app-support
/// directory.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Light, dark, or whatever the OS is wearing. Unset means <see cref="ThemePreference.System"/> —
    /// which is what Cellestra has always done, since its <c>App.axaml</c> requests the Default variant;
    /// what is new is being able to say otherwise, and have it remembered.
    /// </summary>
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>How much of the adaptive command bar a window opens with. Default: the full bar.</summary>
    public CommandBarVisibility CommandBar { get; init; }

    /// <summary>The press-and-hold radial command menu's segment count, labels, and per-segment bindings.</summary>
    public RadialMenuConfig RadialMenu { get; init; } = RadialMenuConfig.Default;

    public double Zoom { get; init; } = 1.0;

    /// <summary>
    /// Seconds of no editing before the workbook is written back to its own file; 0 turns auto-save off.
    /// PRD §9 listed autosave as a v1 non-goal on the reasoning that manual save plus the unsaved-changes
    /// warnings were enough; this supersedes that. The write itself is the same atomic temp-then-rename as
    /// ⌘S, so the "never corrupt an .ods mid-write" guarantee the PRD does insist on is unchanged.
    /// </summary>
    public int AutoSaveSeconds { get; init; } = AutoSave.DefaultSeconds;

    public double WindowWidth { get; init; } = 1100;

    public double WindowHeight { get; init; } = 720;

    public List<string> RecentFiles { get; init; } = [];

    public const int MaxRecentFiles = 10;

    /// <summary>Return a copy with <paramref name="path"/> promoted to the most-recent slot (deduped, capped).</summary>
    public AppSettings WithRecentFile(string path) =>
        this with { RecentFiles = RecentFilesHelper.Push(RecentFiles, path, MaxRecentFiles) };
}
