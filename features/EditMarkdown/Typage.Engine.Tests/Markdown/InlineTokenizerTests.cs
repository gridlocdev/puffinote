using Typage.Engine.Markdown;
using Xunit;
using static Typage.Engine.Tests.Markdown.TokenizerTestHelpers;

namespace Typage.Engine.Tests.Markdown;

public class InlineTokenizerTests
{
    [Fact]
    public void Bold()
    {
        Assert.Equal(new[] { "bold" }, TextsOfType("a **bold** word", TokenType.Strong));
    }

    [Fact]
    public void Italic()
    {
        Assert.Equal(new[] { "it", "it2" }, TextsOfType("*it* and _it2_", TokenType.Emphasis));
    }

    [Fact]
    public void BoldItalic()
    {
        Assert.Equal(new[] { "x" }, TextsOfType("***x***", TokenType.StrongEmphasis));
    }

    [Fact]
    public void Strikethrough()
    {
        Assert.Equal(new[] { "gone" }, TextsOfType("~~gone~~", TokenType.Strikethrough));
    }

    [Fact]
    public void Highlight()
    {
        Assert.Equal(new[] { "hot" }, TextsOfType("==hot==", TokenType.Highlight));
    }

    [Fact]
    public void InlineCode()
    {
        Assert.Equal(new[] { "x = 1" }, TextsOfType("run `x = 1` now", TokenType.InlineCode));
    }

    [Fact]
    public void InlineCode_WithDoubleBacktickFence()
    {
        Assert.Equal(new[] { "a `b` c" }, TextsOfType("``a `b` c``", TokenType.InlineCode));
    }

    [Fact]
    public void Link_TextAndUrl()
    {
        Assert.Equal(new[] { "https://x.com" }, TextsOfType("[home](https://x.com)", TokenType.LinkUrl));
        Assert.True(HasType("[home](https://x.com)", TokenType.Text));
    }

    [Fact]
    public void Link_WithBoldInText()
    {
        // Emphasis inside link text still tokenizes (link text recurses).
        Assert.Equal(new[] { "bold" }, TextsOfType("[a **bold** b](u)", TokenType.Strong));
    }

    [Fact]
    public void Image()
    {
        Assert.Equal(new[] { "pic.png" }, TextsOfType("![alt](pic.png)", TokenType.ImageUrl));
        Assert.Equal(new[] { "alt" }, TextsOfType("![alt](pic.png)", TokenType.ImageAlt));
    }

    [Fact]
    public void Link_WithTitle_ExcludesTitleFromUrl()
    {
        // The title must not leak into the destination (it would corrupt the href on export).
        Assert.Equal(new[] { "http://y" }, TextsOfType("[x](http://y \"a title\")", TokenType.LinkUrl));
    }

    [Fact]
    public void Image_WithTitle_ExcludesTitleFromUrl()
    {
        Assert.Equal(new[] { "pic.png" }, TextsOfType("![alt](pic.png 'caption')", TokenType.ImageUrl));
    }

    [Fact]
    public void Autolink_Bracketed()
    {
        Assert.Equal(new[] { "https://x.com" }, TextsOfType("<https://x.com>", TokenType.Autolink));
    }

    [Fact]
    public void Autolink_Bare()
    {
        Assert.Equal(new[] { "https://example.com/path" },
            TextsOfType("see https://example.com/path here", TokenType.Autolink));
    }

    [Fact]
    public void Autolink_Bare_TrimsTrailingPunctuation()
    {
        Assert.Equal(new[] { "https://example.com" },
            TextsOfType("go to https://example.com.", TokenType.Autolink));
    }

    [Fact]
    public void Escape_RendersLiteral()
    {
        // \* should not start emphasis.
        Assert.False(HasType(@"\*not italic\*", TokenType.Emphasis));
        Assert.True(HasType(@"\*not italic\*", TokenType.Escape));
    }

    [Fact]
    public void WikiLink()
    {
        Assert.Equal(new[] { "Note Title" }, TextsOfType("see [[Note Title]]", TokenType.WikiLink));
    }

    [Fact]
    public void Embed()
    {
        Assert.Equal(new[] { "image.png" }, TextsOfType("![[image.png]]", TokenType.Embed));
    }

    [Fact]
    public void HardBreak_TwoTrailingSpaces()
    {
        Assert.True(HasType("line one  \nline two", TokenType.HardBreak));
    }

    [Fact]
    public void HardBreak_TrailingBackslash()
    {
        Assert.True(HasType("line one\\\nline two", TokenType.HardBreak));
    }

    [Fact]
    public void HardBreak_SingleTrailingSpace_IsNotABreak()
    {
        Assert.False(HasType("line one \nline two", TokenType.HardBreak));
    }

    [Fact]
    public void HardBreak_NotInHeadings()
    {
        Assert.False(HasType("# heading  \ntext", TokenType.HardBreak));
    }

    // ----- tolerant / malformed (PRD §7.4) -----

    [Fact]
    public void UnmatchedBold_IsLiteralText()
    {
        Assert.False(HasType("a **unclosed bold", TokenType.Strong));
        Assert.True(HasType("a **unclosed bold", TokenType.Text));
    }

    [Fact]
    public void UnmatchedBracket_IsLiteralText()
    {
        Assert.False(HasType("[not a link", TokenType.LinkUrl));
    }

    [Fact]
    public void UnterminatedInlineCode_IsLiteralText()
    {
        Assert.False(HasType("a `unterminated", TokenType.InlineCode));
    }

    [Fact]
    public void EmptyEmphasis_IsLiteral()
    {
        Assert.False(HasType("****", TokenType.Strong));
    }
}
