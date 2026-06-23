using Typage.Engine.Document;
using Xunit;

namespace Typage.Engine.Tests.Document;

public class GraphemeTests
{
    [Fact]
    public void Ascii_MovesOneCodeUnit()
    {
        Assert.Equal(1, Grapheme.NextBoundary("abc", 0));
        Assert.Equal(1, Grapheme.PreviousBoundary("abc", 2));
        Assert.Equal(2, Grapheme.PreviousBoundary("abc", 3));
    }

    [Fact]
    public void SurrogatePair_MovesAsOneUnit()
    {
        // U+1F600 grinning face is a surrogate pair (2 UTF-16 code units).
        var s = "a\U0001F600b";
        Assert.Equal(1, Grapheme.NextBoundary(s, 0)); // past 'a'
        Assert.Equal(3, Grapheme.NextBoundary(s, 1)); // emoji is one grapheme, skip 2 units
        Assert.Equal(1, Grapheme.PreviousBoundary(s, 3)); // back over the emoji to after 'a'
    }

    [Fact]
    public void CombiningMark_MovesAsOneUnit()
    {
        var s = "éx"; // e + combining acute accent
        Assert.Equal(2, Grapheme.NextBoundary(s, 0)); // skip base + combining mark together
        Assert.Equal(0, Grapheme.PreviousBoundary(s, 2));
    }

    [Fact]
    public void Crlf_IsOneGrapheme()
    {
        var s = "a\r\nb";
        Assert.Equal(3, Grapheme.NextBoundary(s, 1)); // "\r\n" treated as a single cluster
    }

    [Fact]
    public void Boundaries_ClampAtEnds()
    {
        Assert.Equal(0, Grapheme.PreviousBoundary("abc", 0));
        Assert.Equal(3, Grapheme.NextBoundary("abc", 3));
        Assert.Equal(3, Grapheme.NextBoundary("abc", 99));
    }

    [Fact]
    public void Count_CountsClusters()
    {
        Assert.Equal(3, Grapheme.Count("a\U0001F600b")); // a, emoji, b
        Assert.Equal(1, Grapheme.Count("é"));      // accented e
        Assert.Equal(0, Grapheme.Count(""));
    }
}
