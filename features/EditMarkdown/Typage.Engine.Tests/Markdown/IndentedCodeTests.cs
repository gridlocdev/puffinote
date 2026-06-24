using Typage.Engine.Markdown;
using Xunit;
using static Typage.Engine.Tests.Markdown.TokenizerTestHelpers;

namespace Typage.Engine.Tests.Markdown;

/// <summary>
/// Indented (4-space) code blocks (CommonMark §4.4): a non-blank line indented at least four spaces
/// at a fresh block position is code; it cannot interrupt a paragraph, and blank lines inside it keep
/// the block open.
/// </summary>
public class IndentedCodeTests
{
    [Fact]
    public void FourSpaceIndentAfterBlank_IsCode()
    {
        var md = "para\n\n    code here\nback to prose";
        Assert.Equal(new[] { "    code here" }, TextsOfType(md, TokenType.CodeText));
    }

    [Fact]
    public void IndentedFirstLineOfDocument_IsCode()
    {
        Assert.Equal(new[] { "    int x = 1;" }, TextsOfType("    int x = 1;\nprose", TokenType.CodeText));
    }

    [Fact]
    public void IndentedContinuationOfParagraph_IsNotCode()
    {
        // No blank line above: the indented line is a lazy paragraph continuation, not code.
        Assert.False(HasType("a paragraph\n    still the paragraph", TokenType.CodeText));
    }

    [Fact]
    public void BlankLineInsideBlock_KeepsItOpen()
    {
        var md = "x\n\n    line one\n\n    line three\nprose";
        Assert.Equal(new[] { "    line one", "    line three" }, TextsOfType(md, TokenType.CodeText));
    }

    [Fact]
    public void DedentEndsTheBlock()
    {
        var md = "x\n\n    code\n  not code (two spaces)";
        Assert.Equal(new[] { "    code" }, TextsOfType(md, TokenType.CodeText));
        // The dedented line tokenizes normally — as paragraph text, not code.
        Assert.Contains(TokenType.Text, TypesIn(md));
    }

    [Fact]
    public void EnteringState_TracksTheCodeBlock()
    {
        var (_, model) = Tokenize("x\n\n    code\nprose");
        Assert.Equal(BlockKind.IndentedCode, model.EnteringState(3).Kind);
    }
}
