using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Paragraph join (item 2): consecutive plain-paragraph source lines flow into one block — the
/// interior newline renders as a space and the text wraps as a single paragraph, like a normal
/// document editor. Blank lines, headings, lists, quotes, code, and hard breaks all break the flow.
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
        Assert.Equal(3, e.TotalRowsForTest()); // first | blank | second
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
