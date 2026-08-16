namespace Puffinote.Desktop.Shell;

/// <summary>
/// Which entries of a notebook survive a name filter (PRD §7.2.3) — the answer the sidebar's Filter panel
/// hands its file tree. Pure filesystem logic, no Avalonia, so the rule below is unit-testable without a
/// window.
///
/// <para><b>The rule.</b> An entry <em>matches</em> when its name contains the typed characters, compared
/// case-insensitively and against the <b>whole</b> name — extension included, whether or not the tree is
/// currently drawing suffixes, because "md" is exactly the sort of thing a reader types to find their notes.
/// A matching entry is kept. So are:</para>
/// <list type="bullet">
///   <item>the <b>folders above a match</b>, or there would be no tree to hang it from — a filtered tree is
///   still a tree, and a match four levels down has to be shown where it lives;</item>
///   <item>everything <b>inside a matching folder</b>, because a folder in a file tree <i>is</i> its
///   contents. Kept as a leaf you cannot open, a folder you searched for by name would be the one result
///   the panel refuses to show you.</item>
/// </list>
///
/// <para><see cref="FoldersToOpen"/> is the narrower set: the folders with a match somewhere <em>below</em>
/// them, which the tree opens so the matches are on screen without a click. A folder kept only for its
/// contents (the second case above) is not in it — it opens on the twisty like any other, so filtering for
/// a directory full of notes doesn't unroll all of it at once.</para>
/// </summary>
internal sealed class FileTreeFilter
{
    private readonly HashSet<string> _keep = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);

    private FileTreeFilter(string query) => Query = query;

    /// <summary>The typed characters, trimmed — what <see cref="Matches"/> looks for.</summary>
    public string Query { get; }

    /// <summary>How many entries matched by name. Folders kept for context or contents don't count.</summary>
    public int MatchCount { get; private set; }

    /// <summary>The folders the tree should open, so every match is a visible row (see the class remarks).</summary>
    public IReadOnlyCollection<string> FoldersToOpen => _open;

    /// <summary>Whether <paramref name="path"/> is a row the filtered tree still shows.</summary>
    public bool Keeps(string path) => _keep.Contains(path);

    /// <summary>
    /// Walk <paramref name="root"/> and work out what a filter for <paramref name="query"/> keeps, or return
    /// null when there is nothing to filter — no notebook, or a query of only whitespace, which means "show
    /// everything" rather than "show nothing".
    ///
    /// <para>This reads the whole notebook off disk, so callers run it off the UI thread (see
    /// <c>FilterPanel</c>). It touches no Avalonia state and holds nothing but paths, which is what makes
    /// that safe.</para>
    /// </summary>
    public static FileTreeFilter? Build(NotebookNode? root, string query)
    {
        var trimmed = query.Trim();
        if (root is null || trimmed.Length == 0)
        {
            return null;
        }

        var filter = new FileTreeFilter(trimmed);
        filter.Walk(root, insideMatch: false);
        return filter;
    }

    /// <summary>
    /// Whether <paramref name="name"/> contains <paramref name="query"/>, ignoring case. A substring test
    /// rather than a fuzzy or prefix one: this is a filter, so what it shows has to be explainable by looking
    /// at the rows — every name on screen has the typed characters in it, in that order, side by side. (Quick
    /// Open, ⌘O, is the fuzzy one.)
    /// </summary>
    public static bool Matches(string name, string query) =>
        query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records what <paramref name="directory"/>'s children contribute, and answers whether any match lies at
    /// or below it — which is what makes the folders on the way to a match keep themselves.
    ///
    /// <para>Everything under a matching folder is walked too, rather than swept in wholesale: a deeper match
    /// still needs counting, and its own ancestors still need opening, or a match nested inside a matched
    /// folder would arrive collapsed and uncounted.</para>
    /// </summary>
    private bool Walk(NotebookNode directory, bool insideMatch)
    {
        var matchBelow = false;
        foreach (var child in directory.EnumerateChildren())
        {
            var self = Matches(child.Name, Query);
            if (self)
            {
                MatchCount++;
            }

            var matchInside = child.IsDirectory && Walk(child, insideMatch || self);
            if (self || matchInside || insideMatch)
            {
                _keep.Add(child.Path);
            }

            if (matchInside)
            {
                _open.Add(child.Path);
            }

            matchBelow |= self || matchInside;
        }

        return matchBelow;
    }
}
