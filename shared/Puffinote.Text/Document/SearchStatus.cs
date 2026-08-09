namespace Puffinote.Text.Document;

/// <summary>
/// How a find bar reports where it has got to: "3 of 17", "No results", or nothing at all while there is
/// no query to answer.
/// <para>
/// This is here, and the find <em>bars</em> are not, on purpose. The rule-of-three review both proposals
/// deferred to Phase 5 found the two bars genuinely different — Typage's is a controller bound to its
/// editor that also replaces text and reveals matches through concealed syntax, Plaintain's is a passive
/// strip that raises events at a host — and promoting them would have meant one control with two modes,
/// which is two controls with extra steps. What <em>is</em> duplicated is this: the words the reader
/// actually sees. Two apps showed them, they agreed exactly, and nothing but luck kept them agreeing.
/// The family's shared-vocabulary rule (root AGENTS.md) says an element that appears in more than one app
/// has one name; a phrase it shows has one wording for the same reason.
/// </para>
/// </summary>
public static class SearchStatus
{
    /// <summary>
    /// The label for <paramref name="current"/> (zero-based, -1 for none) out of <paramref name="total"/>
    /// matches of <paramref name="query"/>. Empty for an empty query: a find bar you have just opened has
    /// not failed to find anything, it has not been asked yet, and "No results" would be a lie.
    /// </summary>
    public static string Describe(int current, int total, string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return string.Empty;
        }

        return total == 0 ? "No results" : $"{Math.Max(0, current) + 1} of {total}";
    }
}
