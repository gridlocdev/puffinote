using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Inserting a link/image expands to consume the word under a bare caret (or the selection), and when the
/// target already contains a link/image the inner one is reduced to its text so the outer construct wins
/// (no nesting). With nothing to consume, the caret sits in the visible text slot rather than the URL slot.
/// </summary>
public class LinkExpandTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    [AvaloniaFact]
    public void Link_WithBareCaretInsideAWord_ConsumesTheWord()
    {
        var e = At("see google here", caret: 7); // inside "google"
        e.InsertLink();
        Assert.Equal("see [google]() here", e.Text);
    }

    [AvaloniaFact]
    public void Link_WithASelection_UsesItAsTheText()
    {
        var e = new MarkdownEditor { Text = "click here now" };
        e.Buffer.Selection = new Selection(6, 10); // "here"
        e.InsertLink();
        Assert.Equal("click [here]() now", e.Text);
    }

    [AvaloniaFact]
    public void Link_OnEmptySpot_LeavesCaretInTheTextSlot()
    {
        var e = At("a  b", caret: 2); // between the spaces, no word
        e.InsertLink();
        Assert.Equal("a []() b", e.Text);
        Assert.Equal(3, e.Buffer.Selection.Caret); // inside the [] text brackets
    }

    [AvaloniaFact]
    public void Link_WithText_LeavesCaretInTheUrlSlot()
    {
        var e = At("word", caret: 2);
        e.InsertLink();
        Assert.Equal("[word]()", e.Text);
        Assert.Equal(7, e.Buffer.Selection.Caret); // between the ( )
    }

    [AvaloniaFact]
    public void Link_OverASelectionContainingALink_RemovesTheInnerLink()
    {
        // Outer precedence: the inner [foo](bar) collapses to "foo" inside the new outer link.
        var e = new MarkdownEditor { Text = "a [foo](bar) b" };
        e.Buffer.Selection = new Selection(0, 14); // whole line
        e.InsertLink();
        Assert.Equal("[a foo b]()", e.Text);
    }

    [AvaloniaFact]
    public void Link_WithCaretInsideAnExistingLink_RelinksTheWholeConstruct()
    {
        var e = At("[foo](bar)", caret: 2); // caret inside "foo"
        e.InsertLink();
        Assert.Equal("[foo]()", e.Text); // grew to the whole link, stripped it, re-wrapped
    }

    [AvaloniaFact]
    public void Image_OverASelectionContainingAnImage_RemovesTheInnerImage()
    {
        var e = new MarkdownEditor { Text = "x ![alt](pic.png) y" };
        e.Buffer.Selection = new Selection(0, 19);
        e.InsertImage();
        Assert.Equal("![x alt y]()", e.Text);
    }

    [AvaloniaFact]
    public void ImageReference_UsesTheWordAsAltAndPointsAtTheUrl()
    {
        var e = At("logo", caret: 2);
        e.InsertImageReference("images/logo.png");
        Assert.Equal("![logo](images/logo.png)", e.Text);
    }

    [AvaloniaFact]
    public void ImageReference_FallsBackToTheFileNameWhenThereIsNoWord()
    {
        var e = At("a  b", caret: 2);
        e.InsertImageReference("images/logo.png", altFallback: "logo.png");
        Assert.Equal("a ![logo.png](images/logo.png) b", e.Text);
    }
}
