using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Paragraph join (item 2): consecutive plain-paragraph source lines flow into one block — the
/// interior newline renders as a space and the text wraps as a single paragraph, like a normal
/// document editor. A plain continuation line also flows into a list item (bulleted / numbered / task)
/// above it, so a hard-wrapped item reads as one flowing item (CommonMark lazy continuation). Blank
/// lines, headings, blockquotes, code, indented lines, another marked item, and hard breaks break the flow.
/// </summary>
public class ParagraphJoinTests
{
    private static MarkdownEditor Editor(string text)
    {
        var e = new MarkdownEditor { Text = text };
        return e;
    }

    [AvaloniaFact]
    public void ConsecutiveParagraphLines_ShareOneBlock()
    {
        var e = Editor("first line\nsecond line");
        Assert.Equal(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void JoinedBlock_IsOneFlowingRow_WithTheNewlineAsASpace()
    {
        var e = Editor("first line\nsecond line");
        // No viewport → no wrap, so the joined paragraph is a single row...
        Assert.Equal(1, e.TotalRowsForTest());
        // ...whose content length spans both lines plus the one joining space.
        Assert.Equal("first line".Length + 1 + "second line".Length, e.BlockLengthForTest(0));
    }

    [AvaloniaFact]
    public void BlankLine_SeparatesParagraphsIntoDifferentBlocks()
    {
        var e = Editor("first\n\nsecond");
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(2));
        // The blank line is a soft paragraph separator: collapsed to zero rows, so the two paragraphs sit
        // one line apart (regular spacing) rather than leaving an empty line between them.
        Assert.Equal(2, e.TotalRowsForTest()); // first | second
    }

    [AvaloniaFact]
    public void HeadingDoesNotJoinWithTheFollowingParagraph()
    {
        var e = Editor("# Title\nbody text");
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void ListItemsDoNotJoin()
    {
        var e = Editor("- one\n- two");
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void BulletItem_AbsorbsAPlainContinuationLine()
    {
        // A hard-wrapped bullet: the unmarked second line lazily continues the item, flowing into one row.
        var e = Editor("- item one\ncontinuation");
        Assert.Equal(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
        Assert.Equal(1, e.TotalRowsForTest());
        Assert.Equal("- item one".Length + 1 + "continuation".Length, e.BlockLengthForTest(0));
    }

    [AvaloniaFact]
    public void NumberedItem_AbsorbsAPlainContinuationLine()
    {
        var e = Editor("1. item one\ncontinuation");
        Assert.Equal(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
        Assert.Equal(1, e.TotalRowsForTest());
    }

    [AvaloniaFact]
    public void TaskItem_AbsorbsAPlainContinuationLine()
    {
        var e = Editor("- [ ] task one\ncontinuation");
        Assert.Equal(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
        Assert.Equal(1, e.TotalRowsForTest());
    }

    [AvaloniaFact]
    public void ListItem_AbsorbsSeveralContinuationLines()
    {
        var e = Editor("- item\ncont one\ncont two");
        Assert.Equal(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
        Assert.Equal(e.BlockOfLineForTest(0), e.BlockOfLineForTest(2));
        Assert.Equal(1, e.TotalRowsForTest());
    }

    [AvaloniaFact]
    public void AnotherMarkedItem_DoesNotJoinTheItemAbove()
    {
        // Only an *unmarked* line continues an item; a line that carries its own marker is a new item.
        var e = Editor("- one\n1. two");
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void IndentedLineUnderAnItem_StaysSeparate()
    {
        // Leading indentation is deliberate, so an indented line under an item is laid out on its own row.
        var e = Editor("- item\n  indented");
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void BlockquoteDoesNotAbsorbAPlainContinuationLine()
    {
        var e = Editor("> quote\nplain");
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void ClickingInTheSecondJoinedLine_ResolvesToItsOffset()
    {
        var e = Editor("alpha\nbravo");
        // Offset 8 is the 'a' in "bravo" (alpha=0..4, \n=5, bravo=6..10) — round-trip it through the map.
        var x = e.BuildVisualLineForTest(0).ColumnToX(8); // block column 8
        Assert.Equal(8, e.OffsetAtForTest(0, x));
    }

    [AvaloniaFact]
    public void BackspaceAtTheJoinBoundary_MergesTheSourceLines()
    {
        var e = Editor("ab\ncd");
        e.Buffer.Selection = Selection.At(3); // visual start of the second line
        e.BackspaceForTest();
        Assert.Equal("abcd", e.Text); // the joining newline was removed
    }

    [AvaloniaFact]
    public void IndentedLineBelowProse_DoesNotJoin()
    {
        var e = Editor("hello\n\tworld"); // the second line is tab-indented
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void SpaceIndentedLineBelowProse_DoesNotJoin()
    {
        var e = Editor("hello\n  world"); // leading spaces are deliberate indentation
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void ProseBelowAnIndentedLine_DoesNotAbsorbIt()
    {
        var e = Editor("\tindented\nplain"); // the indented line shouldn't pull "plain" up either
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }

    [AvaloniaFact]
    public void HardBreakKeepsLinesSeparate()
    {
        // Two trailing spaces after "first" make a hard break, so the next line stays a separate block.
        var e = Editor("first  \nsecond");
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(1));
    }
}
