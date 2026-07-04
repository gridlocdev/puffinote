using System.Text.Json;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Search;

/// <summary>
/// Persists the notebook search index to <c>.puffinote/search-index.json</c> so re-opening a notebook reuses the
/// text of unchanged files instead of re-reading (and re-parsing every workbook) from scratch (PRD §7.1 —
/// the search index is volatile per-notebook state). Paths are stored relative to the notebook root; a companion
/// <c>.puffinote/.gitignore</c> keeps this volatile state (and the session) out of the notebook's git while
/// leaving durable settings committable. All I/O is best-effort: a missing/corrupt/old-version cache simply
/// yields an empty index and a full rebuild.
/// </summary>
internal static class SearchIndexCache
{
    private const int CurrentVersion = 1;

    private sealed record Entry(string Path, long MTime, long Size, string Text);

    private sealed record CacheDocument(int Version, List<Entry> Files);

    private static string CachePath(string root) =>
        System.IO.Path.Combine(root, NotebookNode.StateDirectoryName, "search-index.json");

    /// <summary>Load the cached index for <paramref name="root"/> (absolute paths), or empty if unusable.</summary>
    public static IReadOnlyList<IndexedFile> Load(string root)
    {
        try
        {
            var path = CachePath(root);
            if (!File.Exists(path))
            {
                return [];
            }

            var document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(path));
            if (document is null || document.Version != CurrentVersion || document.Files is null)
            {
                return [];
            }

            return [.. document.Files.Select(e => new IndexedFile(System.IO.Path.Combine(root, e.Path), e.Text, e.MTime, e.Size))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Write <paramref name="index"/> to the cache for <paramref name="root"/> (best-effort).</summary>
    public static void Save(string root, IReadOnlyList<IndexedFile> index)
    {
        try
        {
            var stateDir = System.IO.Path.Combine(root, NotebookNode.StateDirectoryName);
            Directory.CreateDirectory(stateDir);
            EnsureGitignore(stateDir);

            var document = new CacheDocument(
                CurrentVersion,
                [.. index.Select(f => new Entry(System.IO.Path.GetRelativePath(root, f.Path), f.MTimeTicks, f.Size, f.Text))]);
            File.WriteAllText(CachePath(root), JsonSerializer.Serialize(document));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A cache write failure is non-fatal — search still works off the in-memory index.
        }
    }

    // PRD §7.1: ignore the volatile index/session, but leave durable settings committable — so we list the
    // specific volatile files rather than ignoring the whole .puffinote/ directory.
    private static void EnsureGitignore(string stateDir)
    {
        var path = System.IO.Path.Combine(stateDir, ".gitignore");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "search-index.json\nsession.json\n");
        }
    }
}
