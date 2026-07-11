using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Nesting a numbered item (Tab/Shift+Tab) keeps the SOURCE numbering in step with the preview: each nesting
/// level restarts at 1, and items whose level changed are renumbered — all in a single undo step.
/// </summary>
public class OrderedRenumberOnNestTests
{
    private static MarkdownEditor At(string text, int line, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    [AvaloniaFact]
    public void IndentLastItem_RestartsItAtOne()
    {
        var e = At("1. a\n2. b\n3. c", 2); // caret on "c"
        e.TabForTest(false);
        Assert.Equal("1. a\n2. b\n    1. c", e.Text);
    }

    [AvaloniaFact]
    public void IndentMiddleItem_RestartsItAndRenumbersThoseAfter()
    {
        var e = At("1. a\n2. b\n3. c", 1); // caret on "b"
        e.TabForTest(false);
        Assert.Equal("1. a\n    1. b\n2. c", e.Text); // b nested -> 1; c becomes top-level 2
    }

    [AvaloniaFact]
    public void OutdentNestedItem_RenumbersBothLevels()
    {
        var e = At("1. a\n    1. b\n    2. c\n2. d", 1); // outdent "b" back to the top level
        e.TabForTest(true);
        Assert.Equal("1. a\n2. b\n    1. c\n3. d", e.Text); // b joins top (2); c restarts (1); d -> 3
    }

    [AvaloniaFact]
    public void EnterThenTab_NestsTheNewItemAtOne()
    {
        var e = At("1. a", 0, col: 4); // caret at end of "1. a"
        e.EnterForTest();              // -> "1. a\n2. "
        e.Buffer.Insert("b");
        e.TabForTest(false);           // nest the new item
        Assert.Equal("1. a\n    1. b", e.Text);
    }

    [AvaloniaFact]
    public void ShiftTabExit_RenumbersTheRemainingList()
    {
        var e = At("1. a\n2. b\n3. c", 1, col: 3); // caret in "b"; outermost -> exits the item
        e.TabForTest(true);
        // "b" loses its marker and becomes prose, which the surface flows into item a — so the list is now
        // "a b" and "c", and c renumbers from 3 to 2. It doesn't restart at 1: prose inside a list continues
        // the item above it rather than ending the list.
        Assert.Equal("1. a\nb\n2. c", e.Text);
    }

    [AvaloniaFact]
    public void DeepNesting_RestartsEachLevel()
    {
        var e = At("1. a\n    1. b\n2. c", 2); // indent "c" so it nests under b's level
        e.TabForTest(false);
        Assert.Equal("1. a\n    1. b\n    2. c", e.Text); // c joins b's sublist as item 2
    }

    [AvaloniaFact]
    public void IndentIsASingleUndoStep()
    {
        var e = At("1. a\n2. b\n3. c", 1);
        e.TabForTest(false);
        Assert.Equal("1. a\n    1. b\n2. c", e.Text);
        e.Buffer.Undo();
        Assert.Equal("1. a\n2. b\n3. c", e.Text); // one undo restores indent AND numbering
    }

    [AvaloniaFact]
    public void IndentingABulletList_DoesNotInventNumbers()
    {
        var e = At("- a\n- b\n- c", 1);
        e.TabForTest(false);
        Assert.Equal("- a\n    - b\n- c", e.Text); // bullets are untouched by renumbering (4-space indent)
    }

    [AvaloniaFact]
    public void CaretStaysInTheNestedItemsContent()
    {
        var e = At("1. a\n2. b\n3. c", 2, col: 3); // caret on "c" content
        e.TabForTest(false);
        var pos = e.Buffer.Document.GetPosition(e.Buffer.Selection.Caret);
        Assert.Equal(2, pos.Line);
        // "    1. c": indent(4) + "1. " (3) = content starts at column 7; caret was 1 char into "c".
        Assert.Equal(7, pos.Column);
    }
}

/// <summary>Edge cases / cross-feature interactions for the ordered-list source renumbering.</summary>
public class OrderedRenumberEdgeTests
{
    private static MarkdownEditor At(string text, int line, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    [AvaloniaFact]
    public void Indent_PreservesCrlfLineEndings()
    {
        var e = At("1. a\r\n2. b\r\n3. c", 2); // CRLF document
        e.TabForTest(false);
        Assert.Equal("1. a\r\n2. b\r\n    1. c", e.Text); // \r\n kept byte-for-byte
    }

    [AvaloniaFact]
    public void Reorder_PreservesCrlfLineEndings()
    {
        var e = At("1. a\r\n2. b\r\n3. c", 2);
        e.MoveLinesForTest(-1);
        Assert.Equal("1. a\r\n2. c\r\n3. b", e.Text);
    }

    [AvaloniaFact]
    public void MultiLineIndent_RenumbersTheWholeRun()
    {
        // Select b and c (lines 1-2) and indent both; both nest and restart, d stays top-level at 2.
        var e = new MarkdownEditor { Text = "1. a\n2. b\n3. c\n4. d" };
        var doc = e.Buffer.Document;
        e.Buffer.Selection = new Selection(doc.GetLineStart(1), doc.GetLineStart(2) + 4);
        e.TabForTest(false);
        Assert.Equal("1. a\n    1. b\n    2. c\n2. d", e.Text);
    }

    [AvaloniaFact]
    public void ABlankLineSeparatesLists_RenumberStopsAtIt()
    {
        // Two separate numbered lists; indenting in the second must not look across the blank line.
        var e = At("1. a\n2. b\n\n1. x\n2. y", 4); // caret on "y"
        e.TabForTest(false);
        Assert.Equal("1. a\n2. b\n\n1. x\n    1. y", e.Text);
    }
}
