using Puffinote.Text.Document;
using Typage.Engine.Html;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.Engine.Tests;

/// <summary>
/// Setext headings and blockquote nesting depth — the two constructs the tokenizer used to get wrong. Both
/// need context a single line can't supply: a setext heading is made by the line *below* it, and a quote's
/// depth is a count the old flat marker threw away.
/// </summary>
public class SetextAndQuoteDepthTests
{
    private static MarkdownModel Model(string text) => MarkdownModel.Build(TextDocument.FromString(text));

    private static (TokenType Type, string? Meta)? First(MarkdownModel m, int line)
    {
        var toks = m.LineTokens(line);
        return toks.Count == 0 ? null : (toks[0].Type, toks[0].Meta);
    }

    // ----- setext headings -----

    [Theory]
    [InlineData("Title\n=====", 1)]
    [InlineData("Title\n-----", 2)]
    [InlineData("Title\n=", 1)]
    [InlineData("Title\n-", 2)]
    [InlineData("Title\n===   ", 1)] // trailing whitespace is allowed
    [InlineData("  Title\n  ===", 1)] // up to 3 columns of indent on either line
    public void ASetextUnderline_MakesTheLineAboveItAHeading(string source, int expectedLevel)
    {
        var m = Model(source);

        // The heading is shaped exactly like an ATX one — a HeadingMarker carrying the level — except that it is
        // zero-length, because a setext heading has no marker punctuation on its text line. Every consumer reads
        // the level off the first token, so none of them has to know which spelling produced it.
        Assert.Equal((TokenType.HeadingMarker, expectedLevel.ToString()), First(m, 0));
        Assert.Equal(0, m.LineTokens(0)[0].Length);

        // The underline is its own type, so the surface can both conceal it and collapse its row away.
        Assert.Equal(TokenType.SetextUnderline, First(m, 1)!.Value.Type);
    }

    [Fact]
    public void ASetextUnderline_BeatsTheHorizontalRuleRule()
    {
        // The bug: "---" under a paragraph was reaching the tokenizer's horizontal-rule check first, so a setext
        // H2 rendered as body text followed by an <hr>. Under a paragraph, the underline must win.
        var m = Model("Title\n---");
        Assert.Equal((TokenType.HeadingMarker, "2"), First(m, 0));
        Assert.Equal(TokenType.SetextUnderline, First(m, 1)!.Value.Type);
    }

    [Fact]
    public void ALoneRule_IsStillAHorizontalRule()
    {
        // ...but a "---" that follows a blank line has no paragraph to underline, so it stays a rule. This is the
        // line the setext fix must not cross.
        var m = Model("Title\n\n---\n");
        Assert.Equal(TokenType.HorizontalRule, First(m, 2)!.Value.Type);
    }

    [Theory]
    [InlineData("# ATX\n---")]          // a heading is not paragraph text
    [InlineData("> quote\n---")]        // nor is a blockquote
    [InlineData("- item\n---")]         // nor a list item
    [InlineData("\n---")]               // nor a blank line
    [InlineData("```\ncode\n```\n---")] // nor anything inside a fence
    public void OnlyParagraphText_TakesAnUnderline(string source)
    {
        var m = Model(source);
        var underline = m.LineCount - 1;
        Assert.NotEqual(TokenType.SetextUnderline, First(m, underline)!.Value.Type);
    }

    [Fact]
    public void FrontMatter_IsNotASetextHeading()
    {
        // Line 0's "---" opens front matter (a closing fence exists below); it must not be read as the text of a
        // heading underlined by the line beneath it.
        var m = Model("---\ntitle: x\n---\nbody\n");
        Assert.Equal(TokenType.FrontMatterFence, First(m, 0)!.Value.Type);
    }

    [Fact]
    public void SetextHeadings_RenderAsHeadingsInHtml_AndTheUnderlineEmitsNothing()
    {
        var doc = TextDocument.FromString("One\n===\n\nTwo\n---\n");
        var html = HtmlRenderer.RenderBody(doc);

        Assert.Contains("<h1>One</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Two</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<hr>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<p></p>", html, StringComparison.Ordinal); // the underline is not an empty paragraph
    }

    // ----- incremental equivalence: the lookahead reaches backwards, so Update has to as well -----

    [Theory]
    [InlineData("Title\nX", 1, "===")]     // typing an underline makes the line above a heading
    [InlineData("Title\n===", 1, "")]      // deleting it unmakes the heading
    [InlineData("Title\n===\nbody", 2, "more")] // an edit below a pair must not strand it
    [InlineData("a\n===\nb\n===", 2, "c")] // an edit between two pairs
    public void IncrementalUpdate_MatchesAFullRebuild(string source, int editLine, string replacement)
    {
        var doc = TextDocument.FromString(source);
        var model = MarkdownModel.Build(doc);

        var start = doc.GetLineStart(editLine);
        var length = doc.GetLineEnd(editLine) - start;
        var edited = doc.Replace(start, length, replacement);

        var incremental = model.Update(edited, editLine, out _);
        var full = MarkdownModel.Build(edited);

        Assert.Equal(full.AllTokens(), incremental.AllTokens());
    }

    // ----- blockquote depth -----

    [Theory]
    [InlineData("> one", 1)]
    [InlineData(">> two", 2)]
    [InlineData("> > two", 2)]
    [InlineData(">>> three", 3)]
    public void ABlockquoteMarker_CarriesItsNestingDepth(string source, int expectedDepth)
    {
        // The marker used to collapse every '>' into one depthless token, so ">>" was indistinguishable from ">"
        // and nesting could not be drawn.
        var m = Model(source);
        Assert.Equal((TokenType.BlockquoteMarker, expectedDepth.ToString()), First(m, 0));
    }

    [Fact]
    public void NestedQuotes_NestInHtml()
    {
        var doc = TextDocument.FromString("> outer\n>> inner\n");
        var html = HtmlRenderer.RenderBody(doc);

        // The inner quote opens a second <blockquote> inside the first, rather than sitting flat beside it.
        var outer = html.IndexOf("<blockquote>", StringComparison.Ordinal);
        var inner = html.IndexOf("<blockquote>", outer + 1, StringComparison.Ordinal);
        Assert.True(inner > 0, "a nested quote should open a nested <blockquote>");
        Assert.True(html.IndexOf("inner", StringComparison.Ordinal) > inner);
    }
}
