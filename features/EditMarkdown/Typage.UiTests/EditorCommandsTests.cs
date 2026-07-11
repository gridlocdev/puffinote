using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The expanded command surface (item: more editor commands): extra inline/block formatting and
/// document-editing actions exposed as public editor methods (and wired into the command catalog).
/// </summary>
public class EditorCommandsTests
{
    private static MarkdownEditor At(string text, int caret = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    private static MarkdownEditor Sel(string text, int start, int end)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = new Selection(start, end);
        return e;
    }

    // ----- inline / block formatting -----

    [AvaloniaFact]
    public void Highlight_WrapsTheWordUnderTheCaret()
    {
        var e = At("note", caret: 2);
        e.ToggleHighlight();
        Assert.Equal("==note==", e.Text);
    }

    [AvaloniaFact]
    public void Image_WrapsSelectionAndLeavesCaretInUrl()
    {
        var e = Sel("logo", 0, 4);
        e.InsertImage();
        Assert.Equal("![logo]()", e.Text);
        Assert.Equal("![logo](".Length, e.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void CodeBlock_WrapsSelectionInFences()
    {
        var e = Sel("x = 1", 0, 5);
        e.InsertCodeBlock();
        Assert.Equal("```\nx = 1\n```", e.Text);
    }

    [AvaloniaFact]
    public void HorizontalRule_OnAnEmptyLine_BecomesTheRule()
    {
        var e = At("", 0);
        e.InsertHorizontalRule();
        Assert.Equal("---", e.Text);
    }

    [AvaloniaFact]
    public void HorizontalRule_BelowText_InsertsItOnItsOwnLine()
    {
        var e = At("para", caret: 4);
        e.InsertHorizontalRule();
        Assert.Equal("para\n\n---\n", e.Text);
    }

    // ----- line-prefix toggles -----

    [AvaloniaFact]
    public void Blockquote_TogglesOnThenOff()
    {
        var e = At("hello", caret: 0);
        e.ToggleBlockquote();
        Assert.Equal("> hello", e.Text);
        e.ToggleBlockquote();
        Assert.Equal("hello", e.Text);
    }

    [AvaloniaFact]
    public void BulletList_TogglesOnThenOff()
    {
        var e = At("item", caret: 0);
        e.ToggleBulletList();
        Assert.Equal("- item", e.Text);
        e.ToggleBulletList();
        Assert.Equal("item", e.Text);
    }

    [AvaloniaFact]
    public void NumberedList_NumbersSelectedParagraphsSequentially()
    {
        // One marker per *block*. Blank lines separate these into three paragraphs, so each becomes an item.
        var e = Sel("a\n\nb\n\nc", 0, 7);
        e.ToggleNumberedList();
        Assert.Equal("1. a\n\n2. b\n\n3. c", e.Text);
    }

    [AvaloniaFact]
    public void BulletList_ConvertsAnExistingNumberedItem()
    {
        var e = At("1. item", caret: 0);
        e.ToggleBulletList();
        Assert.Equal("- item", e.Text);
    }

    // ----- list commands with the caret on a hard-wrapped item's continuation line -----
    // A marker-less line under a list item flows into it, so the reader sees one item. A list command with
    // the caret parked there must retarget the owning item — marking the continuation line instead would
    // splice a rival marker into the middle of the item and tear it in two. Each case is paired with the
    // one-line equivalent it must agree with. (Caret 12 sits inside "wrapped" in every wrapped fixture.)

    [AvaloniaFact]
    public void BulletList_OnAContinuationLine_TogglesTheOwningItem_NotTheWrappedLine()
    {
        var e = At("- apple\nwrapped", caret: 12);
        e.ToggleBulletList();
        Assert.Equal("apple\nwrapped", e.Text); // was "- apple\n- wrapped" — the item split in two
    }

    [AvaloniaFact]
    public void NumberedList_OnAContinuationLine_NumbersTheOwningItem()
    {
        var e = At("- apple\nwrapped", caret: 12);
        e.ToggleNumberedList();
        Assert.Equal("1. apple\nwrapped", e.Text);
    }

    [AvaloniaFact]
    public void BulletList_OverAnItemAndItsContinuation_LeavesTheContinuationUnmarked()
    {
        // The whole wrapped item selected: only the item line carries a marker, so only it toggles.
        var e = Sel("- apple\nwrapped", 0, 15);
        e.ToggleBulletList();
        Assert.Equal("apple\nwrapped", e.Text);
    }

    // ----- a selection spanning a hard-wrapped block's lines -----
    // Consecutive unmarked lines flow into one paragraph on screen, so selecting across them selects one
    // thing. The marker goes on that block's first line only; marking each source line would shatter the
    // paragraph the reader sees into several items.

    [AvaloniaFact]
    public void BulletList_OverAWrappedParagraph_MakesOneItem_NotOnePerLine()
    {
        var e = Sel("a\nb\nc", 0, 5);
        e.ToggleBulletList();
        Assert.Equal("- a\nb\nc", e.Text); // was "- a\n- b\n- c" — one paragraph shattered into three items
    }

    [AvaloniaFact]
    public void NumberedList_OverAWrappedParagraph_MakesOneItem()
    {
        var e = Sel("a\nb\nc", 0, 5);
        e.ToggleNumberedList();
        Assert.Equal("1. a\nb\nc", e.Text);
    }

    [AvaloniaFact]
    public void NumberedList_OverAListWhoseItemIsWrapped_NumbersTheItemsAndSkipsTheContinuation()
    {
        // The wrapped item's continuation is passed over, and numbering counts items rather than lines.
        var e = Sel("- one\nwrap\n- two", 0, 16);
        e.ToggleNumberedList();
        Assert.Equal("1. one\nwrap\n2. two", e.Text);
    }

    // ----- line edit actions -----

    [AvaloniaFact]
    public void DuplicateLine_CopiesTheCaretLineBelow()
    {
        var e = At("alpha\nbravo", caret: 2); // on "alpha"
        e.DuplicateLine();
        Assert.Equal("alpha\nalpha\nbravo", e.Text);
        Assert.Equal(1, e.Buffer.Document.GetPosition(e.Buffer.Selection.Caret).Line); // moved onto the copy
    }

    [AvaloniaFact]
    public void DeleteLine_RemovesTheCaretLine()
    {
        var e = At("alpha\nbravo\ncharlie", caret: 8); // on "bravo"
        e.DeleteLine();
        Assert.Equal("alpha\ncharlie", e.Text);
    }

    [AvaloniaFact]
    public void DeleteLine_OnTheLastLine_RemovesItAndThePrecedingBreak()
    {
        var e = At("alpha\nbravo", caret: 8); // on "bravo" (last line)
        e.DeleteLine();
        Assert.Equal("alpha", e.Text);
    }

    [AvaloniaFact]
    public void SelectCurrentWord_SelectsTheWordUnderTheCaret()
    {
        var e = At("hello world", caret: 8); // inside "world"
        e.SelectCurrentWord();
        Assert.Equal("world", e.Text.Substring(e.Buffer.Selection.Start, e.Buffer.Selection.Length));
    }

    [AvaloniaFact]
    public void Headings_GoUpToLevelSix()
    {
        var e = At("title", caret: 0);
        e.ApplyHeading(6);
        Assert.Equal("###### title", e.Text);
    }
}
