using System.Text.Json;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// The per-notebook UI session (PRD §7.1): the recent-files list and the sidebar's collapsed state and
/// width. Recent paths are <b>absolute</b> here (the shell works in absolute paths); the on-disk form
/// stores them relative to the notebook root so a moved/renamed notebook still resolves them.
/// </summary>
internal sealed record NotebookSession
{
    public IReadOnlyList<string> Recent { get; init; } = [];

    /// <summary>The sidebar's pinned folders (PRD §7.2), in the user's chosen chip order; absolute paths here.</summary>
    public IReadOnlyList<string> Pinned { get; init; } = [];

    public bool SidebarCollapsed { get; init; }

    public double SidebarWidth { get; init; } = SessionStore.DefaultSidebarWidth;
}

/// <summary>
/// Persists <see cref="NotebookSession"/> to <c>.puffinote/session.json</c> (PRD §7.1 — volatile per-notebook
/// state, git-ignored alongside the search index). Mirrors <see cref="Search.SearchIndexCache"/>: recent
/// paths are stored relative to the notebook root, a companion <c>.gitignore</c> keeps the volatile state out
/// of the notebook's git, and all I/O is best-effort — a missing/corrupt/old-version file simply yields the
/// default session so a bad file can never block startup.
/// </summary>
internal static class SessionStore
{
    /// <summary>The sidebar's default pixel width, used when no session pins one.</summary>
    public const double DefaultSidebarWidth = 260;

    private const int CurrentVersion = 1;

    // Pinned is nullable so a session written before pinned folders existed (no field) still loads at Version 1
    // rather than being discarded — an absent list simply means "no pins yet".
    private sealed record SessionDocument(
        int Version, List<string> Recent, bool SidebarCollapsed, double SidebarWidth, List<string>? Pinned = null);

    private static string SessionPath(string root) =>
        System.IO.Path.Combine(root, NotebookNode.StateDirectoryName, "session.json");

    /// <summary>Load the session for <paramref name="root"/> (absolute recent paths), or the default if unusable.</summary>
    public static NotebookSession Load(string root)
    {
        try
        {
            var path = SessionPath(root);
            if (!File.Exists(path))
            {
                return new NotebookSession();
            }

            var document = JsonSerializer.Deserialize<SessionDocument>(File.ReadAllText(path));
            if (document is null || document.Version != CurrentVersion)
            {
                return new NotebookSession();
            }

            List<string> ToAbsolute(IEnumerable<string>? relatives) =>
                [.. (relatives ?? []).Select(rel => System.IO.Path.GetFullPath(System.IO.Path.Combine(root, rel)))];

            return new NotebookSession
            {
                Recent = ToAbsolute(document.Recent),
                Pinned = ToAbsolute(document.Pinned),
                SidebarCollapsed = document.SidebarCollapsed,
                SidebarWidth = document.SidebarWidth > 0 ? document.SidebarWidth : DefaultSidebarWidth,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new NotebookSession();
        }
    }

    /// <summary>Write <paramref name="session"/> for <paramref name="root"/> (best-effort).</summary>
    public static void Save(string root, NotebookSession session)
    {
        try
        {
            var stateDir = System.IO.Path.Combine(root, NotebookNode.StateDirectoryName);
            Directory.CreateDirectory(stateDir);
            EnsureGitignore(stateDir);

            var document = new SessionDocument(
                CurrentVersion,
                [.. session.Recent.Select(p => System.IO.Path.GetRelativePath(root, p))],
                session.SidebarCollapsed,
                session.SidebarWidth,
                [.. session.Pinned.Select(p => System.IO.Path.GetRelativePath(root, p))]);
            File.WriteAllText(SessionPath(root), JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort — an unwritable disk just means the session isn't remembered next time.
        }
    }

    // The same volatile-state ignore list the search index writes (PRD §7.1): ignore the index + session,
    // but leave durable per-notebook settings committable — so we list the files rather than the whole dir.
    private static void EnsureGitignore(string stateDir)
    {
        var path = System.IO.Path.Combine(stateDir, ".gitignore");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "search-index.json\nsession.json\n");
        }
    }
}
