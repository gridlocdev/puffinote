using System.Text.RegularExpressions;
using Puffinote.Text.Document;
using Typage.Engine.Html;
using Xunit;

namespace Typage.Engine.Tests.Html;

public class HtmlRendererTests
{
    private static string Body(string md) => HtmlRenderer.RenderBody(TextDocument.FromString(md));

    [Fact]
    public void Heading()
    {
        Assert.Contains("<h1>Title</h1>", Body("# Title"));
        Assert.Contains("<h3>Sub</h3>", Body("### Sub"));
    }

    [Fact]
    public void Paragraph_WithInline()
    {
        var html = Body("a **b** and *c* and `d`");
        Assert.Contains("<strong>b</strong>", html);
        Assert.Contains("<em>c</em>", html);
        Assert.Contains("<code>d</code>", html);
    }

    [Fact]
    public void Link()
    {
        Assert.Contains("<a href=\"https://x.com\">home</a>", Body("[home](https://x.com)"));
    }

    [Fact]
    public void Image()
    {
        Assert.Contains("<img src=\"p.png\" alt=\"cat\">", Body("![cat](p.png)"));
    }

    [Fact]
    public void UnorderedList()
    {
        var html = Body("- one\n- two");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>one</li>", html);
        Assert.Contains("<li>two</li>", html);
    }

    [Fact]
    public void OrderedList()
    {
        Assert.Contains("<ol>", Body("1. one\n2. two"));
    }

    [Fact]
    public void OrderedList_PreservesStartNumber()
    {
        Assert.Contains("<ol start=\"3\">", Body("3. three\n4. four"));
    }

    [Fact]
    public void NestedList_Nests()
    {
        var html = Body("- a\n  - b\n  - c\n- d");
        // The nested <ul> opens inside the first item, before that item closes.
        var firstItem = html.IndexOf("<li>a", StringComparison.Ordinal);
        var nested = html.IndexOf("<ul>", firstItem, StringComparison.Ordinal);
        var firstClose = html.IndexOf("</li>", firstItem, StringComparison.Ordinal);
        Assert.True(nested > firstItem && nested < firstClose);
        Assert.Contains("<li>b</li>", html);
        Assert.Contains("<li>d</li>", html);
    }

    [Fact]
    public void LooseList_StaysOneListAcrossBlankLine()
    {
        var html = Body("1. one\n\n2. two");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<ol"));
    }

    [Fact]
    public void TaskList_RendersCheckboxes()
    {
        // The export draws the same box the editor does — a state class per item, and the state's own symbol —
        // rather than an <input type=checkbox>, which can only say done or not (PRD §8.3.2).
        var html = Body("- [ ] todo\n- [x] done");
        Assert.Contains("<li class=\"task t-not\">", html);
        Assert.Contains("<li class=\"task t-done\">", html);
        Assert.Contains("aria-label=\"Not started\"", html);
        Assert.Contains("aria-label=\"Done\"", html);
        Assert.Contains("<span class=\"ttext\">todo</span>", html);
    }

    [Fact]
    public void TaskList_RendersEveryState()
    {
        var html = Body("- [ ] a\n- [/] b\n- [x] c");
        foreach (var (cls, label) in new[]
        {
            ("t-not", "Not started"), ("t-prog", "In progress"), ("t-done", "Done"),
        })
        {
            Assert.Contains($"<li class=\"task {cls}\">", html);
            Assert.Contains($"aria-label=\"{label}\"", html);
        }

        // The dash and the check draw a symbol; a resting box is an empty square, so it draws none.
        Assert.Equal(2, Regex.Matches(html, "<svg").Count);
    }

    [Fact]
    public void TheTwoLitStates_ShareOneAccent()
    {
        // Single-hue (PRD §8.3.2): the dash and the check are the same colour, and the done box is filled with
        // it. If a second hue ever creeps back in, this is where it shows up.
        var page = HtmlRenderer.RenderDocument(TextDocument.FromString("- [/] b\n- [x] c"));

        Assert.Contains("li.t-prog > .tbox { color: var(--t-accent); }", page);
        Assert.Contains("background: var(--t-accent); border-color: var(--t-accent)", page);
    }

    [Fact]
    public void Blockquote()
    {
        Assert.Contains("<blockquote>", Body("> quoted"));
    }

    [Fact]
    public void FencedCode_EscapesAndKeepsLanguage()
    {
        var html = Body("```html\n<b>x</b>\n```");
        Assert.Contains("class=\"language-html\"", html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html); // angle brackets escaped, not interpreted
    }

    [Fact]
    public void HorizontalRule()
    {
        Assert.Contains("<hr>", Body("a\n\n---\n\nb"));
    }

    [Fact]
    public void HardBreak_RendersBr()
    {
        Assert.Contains("first<br>\nsecond", Body("first  \nsecond"));
    }

    [Fact]
    public void SoftWrap_DoesNotRenderBr()
    {
        var html = Body("first\nsecond");
        Assert.Contains("first\nsecond", html);
        Assert.DoesNotContain("<br>", html);
    }

    [Fact]
    public void Table()
    {
        var html = Body("| A | B |\n| - | - |\n| 1 | 2 |");
        Assert.Contains("<table>", html);
        Assert.Contains("<th>A</th>", html);
        Assert.Contains("<td>1</td>", html);
    }

    [Fact]
    public void HtmlInText_IsEscaped()
    {
        Assert.Contains("a &lt;script&gt; b", Body("a <script> b"));
    }

    [Fact]
    public void FrontMatter_RendersAsProperties()
    {
        var html = Body("---\ntitle: Hi\n---\n# Body");
        Assert.Contains("<details class=\"properties\">", html);
        Assert.Contains("<h1>Body</h1>", html);
    }

    [Fact]
    public void Document_IsSelfContained()
    {
        var doc = HtmlRenderer.RenderDocument(TextDocument.FromString("# Hi"), "My Doc");
        Assert.StartsWith("<!DOCTYPE html>", doc);
        Assert.Contains("<title>My Doc</title>", doc);
        Assert.Contains("<style>", doc); // styles inlined, no external resources
        Assert.Contains("<h1>Hi</h1>", doc);
    }
}
