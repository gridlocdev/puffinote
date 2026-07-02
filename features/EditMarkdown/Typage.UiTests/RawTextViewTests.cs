using System.Linq;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The plain <see cref="MarkdownEditor.ViewMode.RawText"/> view: the document reads as literal monospace
/// source — markers revealed, no synthetic decorations, no paragraph flow, and blank lines kept as real
/// rows — in contrast to the WYSIWYG <see cref="MarkdownEditor.ViewMode.Rich"/> default.
/// </summary>
public class RawTextViewTests
{
    private static MarkdownEditor Raw(string text)
    {
        var e = new MarkdownEditor { Text = text, View = MarkdownEditor.ViewMode.RawText };
        return e;
    }

    [AvaloniaFact]
    public void RevealsMarkers_AsLiteralText()
    {
        var e = Raw("# Title\n**bold**");
        var heading = e.BuildVisualLineForTest(0);
        Assert.DoesNotContain(heading.Runs, r => r.Kind == VisualRunKind.Concealed);
        Assert.Contains(heading.Runs, r => r.Kind == VisualRunKind.Text && r.Text.Contains("# Title"));

        var bold = e.BuildVisualLineForTest(1);
        Assert.Contains(bold.Runs, r => r.Kind == VisualRunKind.Text && r.Text.Contains("**bold**"));
    }

    [AvaloniaFact]
    public void DrawsNoDecorations()
    {
        // A task item would normally render a checkbox + bullet decoration; raw text shows the literal source.
        var e = Raw("- [x] done");
        var v = e.BuildVisualLineForTest(0);
        Assert.DoesNotContain(v.Runs, r => r.Kind == VisualRunKind.Decoration);
        Assert.Contains(v.Runs, r => r.Kind == VisualRunKind.Text && r.Text.Contains("- [x] done"));
    }

    [AvaloniaFact]
    public void DoesNotFlowParagraphs()
    {
        var e = Raw("first line\nsecond line");
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1)); // each line its own block
        Assert.Equal(2, e.TotalRowsForTest());
    }

    [AvaloniaFact]
    public void KeepsBlankLinesAsRealRows()
    {
        var e = Raw("a\n\nb");
        Assert.Equal(3, e.TotalRowsForTest()); // no soft-collapse of the separator blank
    }

    [AvaloniaFact]
    public void SwitchingBackToRich_RestoresConcealmentAndFlow()
    {
        var e = Raw("first line\nsecond line");
        Assert.Equal(2, e.TotalRowsForTest());

        e.View = MarkdownEditor.ViewMode.Rich;
        Assert.Equal(1, e.TotalRowsForTest()); // paragraphs flow again
        Assert.Equal(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }
}
