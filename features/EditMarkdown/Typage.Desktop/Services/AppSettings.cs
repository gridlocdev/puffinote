using Typage.Control.Controls;

namespace Typage.Desktop.Services;

/// <summary>The appearance mode (PRD §7.2). <see cref="System"/> follows the OS light/dark setting.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
    HighContrast,
}

/// <summary>
/// User preferences persisted as JSON in the OS app-config location (PRD §7.9). A small subset
/// for v1: theme, recent files, tab width, and the formatting-toolbar visibility.
/// </summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public List<string> RecentFiles { get; set; } = new();

    public int TabWidth { get; set; } = 4;

    /// <summary>The left/right text inset (padding) of the editor, in pixels, applied to both sides (PRD §7.5).</summary>
    public int EditorPadding { get; set; } = 20;

    public bool ShowToolbar { get; set; } = true;

    /// <summary>The default for the per-document raw-source view (markers always shown; PRD §7.4).</summary>
    public bool ShowRawSource { get; set; }

    /// <summary>Seconds between crash-recovery autosaves; 0 disables it (PRD §9.4).</summary>
    public int AutosaveSeconds { get; set; } = 30;

    /// <summary>
    /// How a checkbox list re-orders itself when a task is toggled: leave items in place
    /// (<see cref="MarkdownEditor.CompletedTaskSortMode.None"/>, the default) or float completed
    /// items to the top/bottom of their sibling group (PRD §7.2 editing assists).
    /// </summary>
    public MarkdownEditor.CompletedTaskSortMode CompletedTaskSort { get; set; } = MarkdownEditor.CompletedTaskSortMode.None;

    public const int MaxRecentFiles = 10;

    public void PushRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }
}
