using System.Linq;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Plain Enter writes a real Markdown paragraph break (<c>\n\n</c>) instead of a single newline, so the two
/// halves don't flow back together (which made the caret "teleport"). The blank line it creates is a soft
/// separator: collapsed to zero visual rows (regular spacing, no empty line drawn) with the caret gliding
/// over it as one step, and backspace/delete at the seam remove the whole break.
/// </summary>
public class ParagraphBreakTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    [AvaloniaFact]
    public void Enter_InAParagraph_WritesTwoNewlines()
    {
        var e = At("Hello world", 5); // caret after "Hello"
        e.EnterForTest();
        Assert.Equal("Hello\n\n world", e.Text);
        Assert.Equal(7, e.Buffer.Selection.Caret); // start of the new paragraph, past both newlines
    }

    [AvaloniaFact]
    public void TheBlankSeparator_CollapsesToRegularSpacing()
    {
        var e = new MarkdownEditor { Text = "Hello\n\nworld" };
        Assert.Equal(2, e.TotalRowsForTest()); // two paragraphs, no empty row between them
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(2)); // they stay separate paragraphs
    }

    [AvaloniaFact]
    public void Caret_GlidesOverTheCollapsedSeparator_AsOneStep()
    {
        var e = At("Hello\n\nworld", 7); // start of "world"
        e.VisualStepForTest(-1);
        Assert.Equal(5, e.Buffer.Selection.Caret); // jumped over both newlines to the end of "Hello"
        e.VisualStepForTest(1);
        Assert.Equal(7, e.Buffer.Selection.Caret); // and back to the start of "world"
    }

    [AvaloniaFact]
    public void Backspace_AtParagraphStart_RemovesTheWholeBreak()
    {
        var e = At("Hello\n\nworld", 7); // start of "world"
        e.BackspaceForTest();
        Assert.Equal("Helloworld", e.Text); // both newlines gone — not left as a single soft break
        Assert.Equal(5, e.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Delete_AtParagraphEnd_RemovesTheWholeBreak()
    {
        var e = At("Hello\n\nworld", 5); // end of "Hello"
        e.DeleteForTest();
        Assert.Equal("Helloworld", e.Text);
        Assert.Equal(5, e.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Enter_OnABlankLine_AddsASingleLine()
    {
        // Pressing Enter again on the empty line a previous Enter created adds just one newline, so it
        // produces exactly one visible empty line rather than compounding \n\n into a big gap.
        var e = At("A\n\n", 3); // caret on the empty line below "A" (already one visible empty row)
        e.EnterForTest();
        Assert.Equal("A\n\n\n", e.Text);          // one newline added, not two
        Assert.Equal(3, e.TotalRowsForTest());    // "A" then two visible empty lines (one more than before)
    }

    [AvaloniaFact]
    public void ASecondBlankLine_StaysVisible()
    {
        // The first blank after content collapses; a second consecutive blank is a deliberate empty line.
        var e = new MarkdownEditor { Text = "Hello\n\n\nworld" };
        Assert.Equal(3, e.TotalRowsForTest()); // Hello | (one visible empty line) | world
    }

    [AvaloniaFact]
    public void ABlankInsideACodeBlock_IsNotCollapsed()
    {
        var e = new MarkdownEditor { Text = "```\ncode\n\nmore\n```" };
        // The interior blank is part of the code fence, so every row is kept (no paragraph collapse).
        Assert.Equal(5, e.TotalRowsForTest());
    }
}
