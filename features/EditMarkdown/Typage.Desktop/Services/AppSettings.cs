namespace Typage.Desktop.Services;

/// <summary>
/// User preferences persisted as JSON in the OS app-config location (PRD §7.9). A small subset
/// for v1: recent files, tab width, and the formatting-toolbar visibility.
/// </summary>
public sealed class AppSettings
{
    public List<string> RecentFiles { get; set; } = new();

    public int TabWidth { get; set; } = 4;

    public bool ShowToolbar { get; set; } = true;

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
