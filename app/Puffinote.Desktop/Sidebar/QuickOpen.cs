using System.Linq;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// Pure ordering for the quick-open switcher's browse list (PRD §7.2.5). Quick-open reuses the shared
/// <c>PaletteListView</c>, which fuzzy-ranks by name as the user types; this only decides the <em>initial</em>
/// order shown for an empty query — most-recent files first (so the last few documents are one keystroke
/// away), then the rest of the notebook in tree order.
/// </summary>
internal static class QuickOpen
{
    /// <summary>
    /// <paramref name="files"/> reordered so that entries appearing in <paramref name="recent"/> come first
    /// (in recent order), followed by the remaining files in their original order. Recent paths that aren't
    /// in <paramref name="files"/> (e.g. a since-deleted file) are dropped. Comparison is case-insensitive.
    /// </summary>
    public static IReadOnlyList<string> OrderByRecency(IReadOnlyList<string> files, IReadOnlyList<string> recent)
    {
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            byPath.TryAdd(file, file);
        }

        var ordered = new List<string>(files.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in recent)
        {
            if (byPath.TryGetValue(path, out var canonical) && seen.Add(canonical))
            {
                ordered.Add(canonical);
            }
        }

        foreach (var file in files)
        {
            if (seen.Add(file))
            {
                ordered.Add(file);
            }
        }

        return ordered;
    }
}
