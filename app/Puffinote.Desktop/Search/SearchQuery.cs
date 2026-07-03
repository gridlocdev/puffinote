using System.Linq;
using System.Text;

namespace Puffinote.Desktop.Search;

/// <summary>
/// One file in the search index (PRD §7.2.3): its absolute path, the plain text searched, and the
/// last-write time + size used to validate an on-disk cache entry (so unchanged files aren't re-read on the
/// next open). The timestamp/size default to 0 for in-memory-only construction (e.g. tests).
/// </summary>
internal sealed record IndexedFile(string Path, string Text, long MTimeTicks = 0, long Size = 0);

/// <summary>
/// A notebook-search result: the file, its notebook-relative label, how many times the query occurs, and a short
/// snippet around the first occurrence.
/// </summary>
internal sealed record SearchHit(string Path, string RelativePath, int MatchCount, string Snippet);

/// <summary>
/// The pure, testable core of notebook search (PRD §7.2.3): score a set of <see cref="IndexedFile"/> against a
/// query by case-insensitive substring occurrence, ranked most-hits first, each with a one-line snippet.
/// No filesystem or threading here — <see cref="NotebookSearchService"/> supplies the index and runs the build
/// off the UI thread.
/// </summary>
internal static class SearchQuery
{
    private const int SnippetContext = 24;
    private const int SnippetLength = 72;

    public static IReadOnlyList<SearchHit> Run(
        IReadOnlyList<IndexedFile> files, string query, string? root, int maxResults = 200)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<SearchHit>();
        }

        var hits = new List<SearchHit>();
        foreach (var file in files)
        {
            var count = CountOccurrences(file.Text, trimmed);
            if (count > 0)
            {
                hits.Add(new SearchHit(file.Path, Relative(file.Path, root), count, Snippet(file.Text, trimmed)));
            }
        }

        // Most hits first, then alphabetical by label so the order is stable.
        return hits
            .OrderByDescending(h => h.MatchCount)
            .ThenBy(h => h.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private static int CountOccurrences(string text, string query)
    {
        int count = 0;
        int from = 0;
        while (from <= text.Length - query.Length)
        {
            int at = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                break;
            }

            count++;
            from = at + query.Length;
        }

        return count;
    }

    private static string Snippet(string text, string query)
    {
        int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        int start = Math.Max(0, at - SnippetContext);
        int end = Math.Min(text.Length, start + SnippetLength);

        var sb = new StringBuilder();
        if (start > 0)
        {
            sb.Append('…');
        }

        // Collapse runs of whitespace (incl. newlines) to single spaces so the snippet stays one line.
        bool lastWasSpace = false;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        if (end < text.Length)
        {
            sb.Append('…');
        }

        return sb.ToString().Trim();
    }

    private static string Relative(string path, string? root)
    {
        if (root is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return path[root.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar, '/');
        }

        return path;
    }
}
