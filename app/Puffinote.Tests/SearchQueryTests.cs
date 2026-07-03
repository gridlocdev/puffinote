using System.Linq;
using Puffinote.Desktop.Search;
using Xunit;

namespace Puffinote.Tests;

public sealed class SearchQueryTests
{
    private static IndexedFile File(string path, string text) => new(path, text);

    [Fact]
    public void Ranks_by_occurrence_count_and_skips_non_matches()
    {
        var files = new[]
        {
            File("/v/one.md", "todo todo todo"),  // 3 hits
            File("/v/two.md", "a todo here"),      // 1 hit
            File("/v/none.md", "nothing"),         // 0 hits
        };

        var hits = SearchQuery.Run(files, "todo", root: "/v");

        Assert.Equal(new[] { "one.md", "two.md" }, hits.Select(h => h.RelativePath));
        Assert.Equal(3, hits[0].MatchCount);
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var files = new[] { File("/v/n.md", "The Alpha and the ALPHA") };
        Assert.Equal(2, SearchQuery.Run(files, "alpha", "/v").Single().MatchCount);
    }

    [Fact]
    public void An_empty_query_returns_nothing()
    {
        var files = new[] { File("/v/n.md", "content") };
        Assert.Empty(SearchQuery.Run(files, "   ", "/v"));
    }

    [Fact]
    public void Snippet_is_one_line_around_the_first_match()
    {
        var files = new[] { File("/v/n.md", "intro\n\n  the needle is\there\nmore") };
        var snippet = SearchQuery.Run(files, "needle", "/v").Single().Snippet;

        Assert.Contains("needle", snippet);
        Assert.DoesNotContain('\n', snippet);
        Assert.DoesNotContain('\t', snippet);
    }

    [Fact]
    public void Paths_are_reported_relative_to_the_notebook_root()
    {
        var files = new[] { File("/v/sub/deep.md", "found") };
        Assert.Equal("sub/deep.md", SearchQuery.Run(files, "found", "/v").Single().RelativePath);
    }
}
