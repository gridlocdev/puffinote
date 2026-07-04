using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Inline links render like HTML hyperlinks: the clickable text is coloured (link foreground) and
/// underlined, while the surrounding <c>[</c>, <c>](url)</c> syntax conceals. The engine tokenizes link
/// text as ordinary content, so the control colours/underlines whatever falls inside the link-text span.
/// </summary>
public class LinkRenderingTests
{
    private static MarkdownEditor CaretAtEnd(string text)
    {
        var editor = new MarkdownEditor { Text = text };
        editor.Buffer.Selection = Selection.At(editor.Buffer.Document.Length);
        return editor;
    }

    private static VisualRun TextRun(MarkdownEditor editor, int line, string text) =>
        editor.BuildVisualLineForTest(line).Runs.First(r => r.Kind == VisualRunKind.Text && r.Text == text);

    [AvaloniaFact]
    public void InlineLinkText_IsColouredAndUnderlined()
    {
        var theme = MarkdownTheme.Default;
        var editor = CaretAtEnd("see [Google](https://google.com) here");

        var run = TextRun(editor, 0, "Google");
        Assert.Same(theme.LinkForeground, run.Style.Foreground);
        Assert.Same(theme.LinkUnderline, run.Style.Decorations);
    }

    [AvaloniaFact]
    public void TheUrlAndBrackets_AreConcealed()
    {
        var editor = CaretAtEnd("see [Google](https://google.com) here");
        var runs = editor.BuildVisualLineForTest(0).Runs;

        // The visible text never contains the URL or the bracket/paren syntax.
        var visible = string.Concat(runs.Where(r => r.Kind == VisualRunKind.Text).Select(r => r.Text));
        Assert.DoesNotContain("https://google.com", visible);
        Assert.DoesNotContain("](", visible);
        Assert.Contains("Google", visible);
    }

    [AvaloniaFact]
    public void PlainTextOutsideALink_IsNotUnderlined()
    {
        var editor = CaretAtEnd("see [Google](https://google.com) here");
        var run = TextRun(editor, 0, "see ");
        Assert.Null(run.Style.Decorations);
    }

    [AvaloniaFact]
    public void EmphasisInsideALink_KeepsItsTypefaceButGainsLinkColour()
    {
        var theme = MarkdownTheme.Default;
        var editor = CaretAtEnd("[**bold**](url)");

        var run = TextRun(editor, 0, "bold");
        Assert.Same(theme.LinkForeground, run.Style.Foreground);
        Assert.Same(theme.LinkUnderline, run.Style.Decorations);
        Assert.Equal(Avalonia.Media.FontWeight.Bold, run.Style.Typeface.Weight); // still bold
    }
}
