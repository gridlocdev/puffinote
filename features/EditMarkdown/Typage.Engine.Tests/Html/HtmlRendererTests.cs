using Typage.Engine.Document;
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
    public void TaskList_RendersCheckboxes()
    {
        var html = Body("- [ ] todo\n- [x] done");
        Assert.Contains("<input type=\"checkbox\" disabled>", html);
        Assert.Contains("<input type=\"checkbox\" disabled checked>", html);
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
