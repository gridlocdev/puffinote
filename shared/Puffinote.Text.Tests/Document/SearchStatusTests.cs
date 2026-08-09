using Puffinote.Text.Document;
using Xunit;

namespace Puffinote.Text.Tests.Document;

/// <summary>
/// The words a find bar shows. Two apps show them, which is why they live here rather than in either —
/// the Phase 5 rule-of-three review promoted the wording and left the bars where they were.
/// </summary>
public class SearchStatusTests
{
    /// <summary>
    /// A find bar you have just opened has not failed to find anything: it has not been asked. "No
    /// results" there would be a lie, and it is the kind that makes someone retype a query that was fine.
    /// </summary>
    [Fact]
    public void An_empty_query_says_nothing_rather_than_No_results()
    {
        Assert.Equal(string.Empty, SearchStatus.Describe(-1, 0, string.Empty));
        Assert.Equal(string.Empty, SearchStatus.Describe(-1, 0, null));
    }

    [Fact]
    public void A_query_with_no_matches_says_so()
    {
        Assert.Equal("No results", SearchStatus.Describe(-1, 0, "zzz"));
    }

    /// <summary>Counting is one-based for the reader, zero-based for the caller — the off-by-one lives here
    /// once instead of at each call site.</summary>
    [Fact]
    public void The_current_match_is_counted_from_one()
    {
        Assert.Equal("1 of 17", SearchStatus.Describe(0, 17, "the"));
        Assert.Equal("3 of 17", SearchStatus.Describe(2, 17, "the"));
        Assert.Equal("17 of 17", SearchStatus.Describe(16, 17, "the"));
    }

    /// <summary>"None stepped to yet" still has to name a total, not print "0 of 17".</summary>
    [Fact]
    public void Matches_with_nothing_stepped_to_yet_still_start_at_one()
    {
        Assert.Equal("1 of 17", SearchStatus.Describe(-1, 17, "the"));
    }
}
