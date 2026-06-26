using BirdBrain.Components.Commands;
using Xunit;

namespace Typage.UiTests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("Bold", "bold")]
    [InlineData("Bold", "bd")]      // subsequence
    [InlineData("Inline Code", "ic")] // word-start subsequence
    [InlineData("Save As…", "sa")]
    public void Matches_Subsequences(string candidate, string query)
    {
        Assert.True(FuzzyMatcher.IsMatch(candidate, query));
    }

    [Theory]
    [InlineData("Bold", "xyz")]
    [InlineData("Bold", "blz")]
    public void Rejects_NonSubsequences(string candidate, string query)
    {
        Assert.False(FuzzyMatcher.IsMatch(candidate, query));
    }

    [Fact]
    public void EmptyQuery_MatchesEverything()
    {
        Assert.Equal(0, FuzzyMatcher.Score("anything", string.Empty));
    }

    [Fact]
    public void WordStartMatch_RanksHigherThanMidWord()
    {
        // "h" at the start of "Heading 1" should outscore "h" buried in "Strikethrough".
        Assert.True(FuzzyMatcher.Score("Heading 1", "h") > FuzzyMatcher.Score("Strikethrough", "h"));
    }
}
