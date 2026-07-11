using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The surface wiring for the §7.2 editing assists. The decisions live in the engine
/// (LinePrefixTests); these confirm the control routes Enter / Tab / typed pairs through them and
/// applies the resulting edits to the buffer.
/// </summary>
public class EditingAssistsTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var editor = new MarkdownEditor { Text = text };
        editor.Buffer.Selection = Selection.At(caret);
        return editor;
    }

    [AvaloniaFact]
    public void Enter_ContinuesUnorderedList()
    {
        var editor = At("- apple", 7);
        editor.EnterForTest();
        Assert.Equal("- apple\n- ", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_ExitsEmptyItem()
    {
        var editor = At("- apple\n- ", 10);
        editor.EnterForTest();
        Assert.Equal("- apple\n", editor.Text);
    }

    // ----- Enter on a hard-wrapped item's continuation line -----
    // A plain (marker-less) line under a list item lazily flows into that item, so the reader sees one
    // flowing item. Enter with the caret on that continuation line must continue the *owning* item, exactly
    // as it would from the item's own line — not break the flowed item into a list item plus a stray
    // paragraph. Each case pairs the wrapped item with its one-line equivalent to pin that equivalence.

    [AvaloniaFact]
    public void Enter_OnAWrappedBulletsContinuationLine_ContinuesTheBullet()
    {
        var editor = At("- apple\nwrapped", 15);
        editor.EnterForTest();
        Assert.Equal("- apple\nwrapped\n- ", editor.Text); // as "- apple wrapped" would give "- apple wrapped\n- "
    }

    [AvaloniaFact]
    public void Enter_OnAWrappedNumbersContinuationLine_ContinuesTheNumbering()
    {
        var editor = At("1. apple\nwrapped", 16);
        editor.EnterForTest();
        Assert.Equal("1. apple\nwrapped\n2. ", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_OnAWrappedTasksContinuationLine_ContinuesWithAnUncheckedBox()
    {
        var editor = At("- [x] apple\nwrapped", 19);
        editor.EnterForTest();
        Assert.Equal("- [x] apple\nwrapped\n- [ ] ", editor.Text); // a continued task item starts unchecked
    }

    [AvaloniaFact]
    public void Enter_MidwayThroughAContinuationLine_SplitsIntoANewItem()
    {
        // Caret between "wrap" and " ped" — the same split the one-line item would produce, carrying the
        // remainder into a fresh bullet rather than into an unmarked paragraph.
        var editor = At("- apple\nwrap ped", 12);
        editor.EnterForTest();
        Assert.Equal("- apple\nwrap\n-  ped", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_OnTheSecondContinuationLine_StillContinuesTheOwningItem()
    {
        var editor = At("- apple\nwrap one\nwrap two", 25);
        editor.EnterForTest();
        Assert.Equal("- apple\nwrap one\nwrap two\n- ", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_OnAWrappedPlainParagraph_StillBreaksTheParagraph()
    {
        // A plain paragraph takes a continuation too, but it has no marker to continue — Enter must stay a
        // paragraph break, not acquire a bullet from nowhere.
        var editor = At("apple\nwrapped", 13);
        editor.EnterForTest();
        Assert.Equal("apple\nwrapped\n\n", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_LeftOfACheckbox_PushesTheItemDown_WithoutDuplicatingTheMarker()
    {
        // Caret at column 0 — left of the checkbox. Enter must add a blank line above and slide the whole
        // "- [ ] task" item down, NOT splice another "- [ ] " into the line.
        var editor = At("- [ ] task", 0);
        editor.EnterForTest();
        Assert.Equal("\n- [ ] task", editor.Text);
        Assert.Equal(1, editor.Buffer.Selection.Caret); // caret rides the item (start of the pushed-down line)
    }

    [AvaloniaFact]
    public void Enter_LeftOfABullet_PushesTheItemDown()
    {
        var editor = At("- task", 0);
        editor.EnterForTest();
        Assert.Equal("\n- task", editor.Text);
        Assert.Equal(1, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Enter_LeftOfANestedBulletsCheckbox_KeepsTheIndentIntact()
    {
        // Indented item, caret at the marker (past the indent, left of the checkbox). The whole line —
        // indentation and marker — slides down as one; the indent is not split onto the blank line.
        var editor = At("- a\n  - [ ] c", caret: 6); // offset 6 = start of "- [ ] c" marker (after "  ")
        editor.EnterForTest();
        Assert.Equal("- a\n\n  - [ ] c", editor.Text);
        Assert.Equal(7, editor.Buffer.Selection.Caret); // still at the marker, on the pushed-down item
    }

    [AvaloniaFact]
    public void Enter_AtACheckboxsContentStart_StillContinuesTheList()
    {
        // Right of the checkbox (caret at the content "task") keeps the normal split-into-a-new-item assist.
        var editor = At("- [ ] task", 6);
        editor.EnterForTest();
        Assert.Equal("- [ ] \n- [ ] task", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_RenumbersFollowingOrderedItems()
    {
        // Caret at end of "2. b" (offset 9); Enter inserts "3. " and the old "3. d" becomes "4. d".
        var editor = At("1. a\n2. b\n3. d", 9);
        editor.EnterForTest();
        Assert.Equal("1. a\n2. b\n3. \n4. d", editor.Text);
    }

    [AvaloniaFact]
    public void Tab_IndentsListItem()
    {
        var editor = At("- item", 6);
        editor.TabForTest(shift: false);
        Assert.Equal("    - item", editor.Text);
    }

    [AvaloniaFact]
    public void ShiftTab_OutdentsListItem()
    {
        var editor = At("    - item", 10);
        editor.TabForTest(shift: true);
        Assert.Equal("- item", editor.Text);
    }

    [AvaloniaFact]
    public void ShiftTab_AtOutermostLevel_ExitsTheList()
    {
        var editor = At("- item", 6); // a top-level item — nothing left to outdent
        editor.TabForTest(shift: true);
        Assert.Equal("item", editor.Text); // the marker is stripped, the content stays
    }

    [AvaloniaFact]
    public void ShiftTab_OnAnEmptyOutermostItem_OutdentsToAnEmptyLine()
    {
        var editor = At("- ", 2);
        editor.TabForTest(shift: true);
        Assert.Equal("", editor.Text); // exiting an empty item leaves an empty line
    }

    [AvaloniaFact]
    public void ShiftTab_AtOutermostLevel_ExitsATaskItem()
    {
        var editor = At("- [ ] task", 10);
        editor.TabForTest(shift: true);
        Assert.Equal("task", editor.Text);
    }

    [AvaloniaFact]
    public void ShiftTab_AtOutermostLevel_ExitsABlockquote()
    {
        var editor = At("> note", 6);
        editor.TabForTest(shift: true);
        Assert.Equal("note", editor.Text);
    }

    [AvaloniaFact]
    public void ShiftTab_OnANestedItem_OutdentsOneLevel_WithoutExiting()
    {
        var editor = At("    - item", 10); // indent 4 → still room to outdent
        editor.TabForTest(shift: true);
        Assert.Equal("- item", editor.Text); // unchanged: it outdents rather than exiting
    }

    [AvaloniaFact]
    public void Tab_InPlainText_InsertsSpaces()
    {
        var editor = At("abc", 3);
        editor.TabForTest(shift: false);
        Assert.Equal("abc    ", editor.Text);
    }

    [AvaloniaFact]
    public void Type_WrapsSelectionInPair()
    {
        var editor = new MarkdownEditor { Text = "word" };
        editor.Buffer.Selection = new Selection(0, 4);
        editor.TypeForTest('*');
        Assert.Equal("*word*", editor.Text);
        Assert.Equal(1, editor.Buffer.Selection.Start);
        Assert.Equal(5, editor.Buffer.Selection.End);
    }

    [AvaloniaFact]
    public void Type_AutoClosesBracketAtBoundary()
    {
        var editor = At(string.Empty, 0);
        editor.TypeForTest('(');
        Assert.Equal("()", editor.Text);
        Assert.Equal(1, editor.Buffer.Selection.Caret); // caret between the pair
    }

    [AvaloniaFact]
    public void Type_TypesThroughClosingBracket()
    {
        var editor = At("()", 1); // caret between an existing pair
        editor.TypeForTest(')');
        Assert.Equal("()", editor.Text); // no duplicate ')'
        Assert.Equal(2, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Backspace_RemovesBothHalvesOfEmptyPair()
    {
        var editor = At("()", 1);
        editor.BackspaceForTest();
        Assert.Equal(string.Empty, editor.Text);
    }

    [AvaloniaFact]
    public void Type_DoesNotAutoCloseEmphasisMidWord()
    {
        var editor = At("ab", 1); // caret between 'a' and 'b'
        editor.TypeForTest('*');
        Assert.Equal("a*b", editor.Text); // single '*', not "a**b"
    }
}
