using Typage.Engine.Document;
using Typage.Engine.Markdown;
using Xunit;
using static Typage.Engine.Tests.Markdown.TokenizerTestHelpers;

namespace Typage.Engine.Tests.Markdown;

public class MarkdownModelTests
{
    [Theory]
    [InlineData("# H1", "1")]
    [InlineData("### H3", "3")]
    [InlineData("###### H6", "6")]
    public void AtxHeading_EmitsMarkerWithLevel(string md, string level)
    {
        var (_, model) = Tokenize(md);
        var marker = Assert.Single(model.AllTokens(), t => t.Type == TokenType.HeadingMarker);
        Assert.Equal(level, marker.Meta);
    }

    [Fact]
    public void SevenHashes_IsNotAHeading()
    {
        Assert.False(HasType("####### too many", TokenType.HeadingMarker));
    }

    [Fact]
    public void Blockquote_EmitsMarker()
    {
        Assert.Equal(new[] { "> " }, TextsOfType("> quoted", TokenType.BlockquoteMarker));
    }

    [Fact]
    public void UnorderedList_EmitsMarker()
    {
        Assert.Equal(new[] { "- " }, TextsOfType("- item", TokenType.ListMarker));
    }

    [Fact]
    public void OrderedList_EmitsMarker()
    {
        Assert.Equal(new[] { "1. ", "2. " }, TextsOfType("1. a\n2. b", TokenType.ListMarker));
    }

    [Fact]
    public void TaskList_EmitsTaskMarker()
    {
        Assert.Equal(new[] { "[ ] ", "[x] " }, TextsOfType("- [ ] todo\n- [x] done", TokenType.TaskMarker));
    }

    [Fact]
    public void HorizontalRule()
    {
        Assert.True(HasType("---", TokenType.HorizontalRule));
        Assert.True(HasType("***", TokenType.HorizontalRule));
        Assert.True(HasType("___", TokenType.HorizontalRule));
    }

    [Fact]
    public void FencedCode_BodyIsCodeText_AndCarriesState()
    {
        var md = "```python\nx = 1\ny = 2\n```";
        var (_, model) = Tokenize(md);
        Assert.Equal(new[] { "x = 1", "y = 2" }, TextsOfType(md, TokenType.CodeText));
        // Line 1 and 2 are entered inside the fence; line 0 (the opener) is Normal.
        Assert.Equal(BlockKind.Normal, model.EnteringState(0).Kind);
        Assert.Equal(BlockKind.FencedCode, model.EnteringState(1).Kind);
        Assert.Equal(BlockKind.FencedCode, model.EnteringState(2).Kind);
    }

    [Fact]
    public void FencedCode_CapturesLanguage()
    {
        var marker = System.Linq.Enumerable.First(
            Tokenize("```rust\ncode\n```").Model.AllTokens(),
            t => t.Type == TokenType.CodeFence);
        Assert.Equal("rust", marker.Meta);
    }

    [Fact]
    public void FencedCode_DoesNotTokenizeMarkdownInside()
    {
        // The '**' inside a code block must NOT become Strong.
        Assert.False(HasType("```\n**not bold**\n```", TokenType.Strong));
    }

    [Fact]
    public void UnterminatedFence_ExtendsToEof()
    {
        var md = "```\nstill code\nmore code";
        Assert.Equal(new[] { "still code", "more code" }, TextsOfType(md, TokenType.CodeText));
    }

    [Fact]
    public void FrontMatter_OnlyAtTop()
    {
        var (_, model) = Tokenize("---\ntitle: Hi\n---\n# Body");
        Assert.Equal(BlockKind.FrontMatter, model.EnteringState(1).Kind);
        Assert.True(HasType("---\ntitle: Hi\n---\n# Body", TokenType.FrontMatterText));
    }

    [Fact]
    public void DashesNotAtTop_AreHorizontalRule_NotFrontMatter()
    {
        Assert.False(HasType("text\n---\nmore", TokenType.FrontMatterFence));
    }

    [Fact]
    public void Table_IsRecognized()
    {
        var md = "| A | B |\n| - | - |\n| 1 | 2 |";
        Assert.True(HasType(md, TokenType.TableDelimiter));
        // Pipe markers present on the header row.
        Assert.Contains(Tokenize(md).Model.LineTokens(0), t => t.Type == TokenType.Marker);
    }

    [Fact]
    public void PipesWithoutDelimiter_AreNotATable()
    {
        Assert.False(HasType("a | b | c\njust text", TokenType.TableDelimiter));
    }
}
