using System.Linq;
using Typage.Engine.Markdown;
using Xunit;
using static Typage.Engine.Tests.Markdown.TokenizerTestHelpers;

namespace Typage.Engine.Tests.Markdown;

/// <summary>
/// The per-line inline <see cref="Token.Node"/> grouping (PRD §7.4): the markers and content of one
/// inline construct share an id so the live-preview layer can reveal / atomically skip it as a unit;
/// plain text and line-prefix/block markers stay <c>0</c>; nested constructs get distinct ids.
/// </summary>
public class InlineNodeTests
{
    private static System.Collections.Generic.List<Token> Tokens(string markdown)
    {
        var (_, model) = Tokenize(markdown);
        return model.AllTokens().ToList();
    }

    [Fact]
    public void Emphasis_MarkersAndContent_ShareOneNonZeroNode()
    {
        var tokens = Tokens("a **bold** b");
        var construct = tokens.Where(t => t.Type is TokenType.Marker or TokenType.Strong).ToList();
        Assert.All(construct, t => Assert.NotEqual(0, t.Node));
        Assert.Single(construct.Select(t => t.Node).Distinct()); // all the same id
        // The surrounding plain text is not part of a node.
        Assert.All(tokens.Where(t => t.Type == TokenType.Text), t => Assert.Equal(0, t.Node));
    }

    [Fact]
    public void TwoConstructsOnALine_GetDistinctNodes()
    {
        var tokens = Tokens("**a** and *b*");
        var strongNode = tokens.First(t => t.Type == TokenType.Strong).Node;
        var emphasisNode = tokens.First(t => t.Type == TokenType.Emphasis).Node;
        Assert.NotEqual(0, strongNode);
        Assert.NotEqual(0, emphasisNode);
        Assert.NotEqual(strongNode, emphasisNode);
    }

    [Fact]
    public void Link_MarkersShareANode_DistinctFromNestedEmphasis()
    {
        var tokens = Tokens("[a **b** c](http://x.com)");
        // The link's own markers ([, ](, url, closing ) ) share one id.
        var linkNode = tokens.First(t => t.Type == TokenType.LinkUrl).Node;
        Assert.NotEqual(0, linkNode);
        Assert.Equal(linkNode, tokens.First(t => t.Type == TokenType.Marker).Node); // the leading '['
        // The bold nested inside the link text is its own node.
        var boldNode = tokens.First(t => t.Type == TokenType.Strong).Node;
        Assert.NotEqual(0, boldNode);
        Assert.NotEqual(linkNode, boldNode);
    }

    [Fact]
    public void LinePrefixAndBlockMarkers_StayZero()
    {
        Assert.All(
            Tokens("# Heading").Where(t => t.Type == TokenType.HeadingMarker),
            t => Assert.Equal(0, t.Node));
        Assert.All(
            Tokens("- [ ] task").Where(t => t.Type is TokenType.ListMarker or TokenType.TaskMarker),
            t => Assert.Equal(0, t.Node));
        Assert.All(
            Tokens("> quote").Where(t => t.Type == TokenType.BlockquoteMarker),
            t => Assert.Equal(0, t.Node));
    }

    [Fact]
    public void NodeIds_AreLineLocal_RestartingEachLine()
    {
        // Both lines have a single construct; per-line-local ids mean both get the same first id,
        // which is what keeps incremental re-tokenization byte-identical to a full rebuild.
        var tokens = Tokens("*one*\n*two*");
        var nodes = tokens.Where(t => t.Type == TokenType.Emphasis).Select(t => t.Node).ToList();
        Assert.Equal(2, nodes.Count);
        Assert.Equal(nodes[0], nodes[1]);
    }
}
