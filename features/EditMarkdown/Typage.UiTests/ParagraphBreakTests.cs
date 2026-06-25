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
    public void Enter_AtDocumentStart_AddsOnlyOneLine()
    {
        // No content before the caret (and no line above to collapse), so a single newline — not \n\n —
        // pushes the paragraph down by exactly one line.
        var e = At("Helloworld", 0);
        e.EnterForTest();
        Assert.Equal("\nHelloworld", e.Text);
        Assert.Equal(1, e.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Enter_AtAParagraphStart_AddsOneBlankLine()
    {
        // Caret at the start of "world": one newline adds exactly one visible blank line above it (the
        // existing separator already covers paragraph-ness), rather than opening a two-line gap.
        var e = At("Hello\n\nworld", 7);
        e.EnterForTest();
        Assert.Equal("Hello\n\n\nworld", e.Text);
        Assert.Equal(3, e.TotalRowsForTest()); // Hello | one visible empty line | world
    }

    [AvaloniaFact]
    public void Backspace_OnAVisibleEmptyLine_RemovesOneNewline_NotTheWholeBreak()
    {
        // "A | empty | B": backspace on the visible empty line removes one newline, collapsing the two
        // blanks back to one (A and B sit adjacent) — it must NOT remove the whole \n\n and re-flow A and B
        // into a single paragraph.
        var e = At("A\n\n\nB", 3); // start of the visible empty middle line
        e.BackspaceForTest();
        Assert.Equal("A\n\nB", e.Text);
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(2)); // still two paragraphs
    }

    [AvaloniaFact]
    public void Enter_AtEndOfAParagraphAlreadyFollowedByABreak_AddsOnlyOneBlank()
    {
        // "Hello | world" are already separate paragraphs (\n\n). Pressing Enter at the end of the first must
        // add exactly one visible blank line between them, not two — a newline already follows the caret, so
        // only one more is needed, and the caret lands on the visible blank (offset 7), not the collapsed one.
        var e = At("Hello\n\nworld", 5);
        e.EnterForTest();
        Assert.Equal("Hello\n\n\nworld", e.Text);
        Assert.Equal(7, e.Buffer.Selection.Caret);
        Assert.Equal(3, e.TotalRowsForTest()); // Hello | one blank | world
    }

    [AvaloniaFact]
    public void Enter_AtEndThenBackspace_RestoresTheText()
    {
        // The \n\n insert and the collapsed-blank merge are inverses for a mid-document paragraph end.
        var e = At("Hello\n\nworld", 5);
        e.EnterForTest();
        e.BackspaceForTest();
        Assert.Equal("Hello\n\nworld", e.Text);
    }

    [AvaloniaFact]
    public void Enter_AtAFlowJoinedSeam_SplitsIntoAdjacentParagraphs()
    {
        // "ab\ncd" renders as one flowing paragraph; Enter at the soft-break seam splits it into two
        // paragraphs sitting adjacent (one \n\n), with the caret at the start of the second.
        var e = At("ab\ncd", 2);
        e.EnterForTest();
        Assert.Equal("ab\n\ncd", e.Text);
        Assert.Equal(4, e.Buffer.Selection.Caret); // start of "cd"
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

    [AvaloniaFact]
    public void Enter_InACrlfDocument_InsertsCrlf_NotBareLf()
    {
        // A CRLF file must stay all-CRLF after editing (golden rule #5) — Enter must not splice in a bare \n.
        var e = At("alpha\r\nbeta", 5); // end of "alpha", before the \r
        e.EnterForTest();
        Assert.Equal("alpha\r\n\r\nbeta", e.Text); // one CRLF blank, the soft break becomes a paragraph split
        Assert.DoesNotContain('\n', e.Text.Replace("\r\n", "")); // no lone LF survives
        Assert.Equal(9, e.Buffer.Selection.Caret); // start of "beta" (past both CRLF breaks)
    }

    [AvaloniaFact]
    public void Enter_MidParagraphInCrlf_OpensACrlfParagraphBreak()
    {
        // The doc's CRLF is detected from its existing line; splitting mid-word writes a CRLF paragraph break.
        var e = new MarkdownEditor { Text = "alpha beta\r\nx" };
        e.Buffer.Selection = Selection.At(5); // after "alpha"
        e.EnterForTest();
        Assert.Equal("alpha\r\n\r\n beta\r\nx", e.Text);
    }

    [AvaloniaFact]
    public void ShiftEnter_InACrlfDocument_InsertsCrlf()
    {
        var e = new MarkdownEditor { Text = "alpha\r\nbeta" };
        e.Buffer.Selection = Selection.At(5);
        e.ShiftEnterForTest();
        Assert.Equal("alpha\r\n\r\nbeta", e.Text);
    }
}
