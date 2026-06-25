using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Alt+Shift reorder carries a moved list item together with its nested sub-items of ANY kind (bulleted /
/// numbered / checkbox) and plain continuation text. It does NOT drag along a nested block construct — a
/// heading, blockquote, horizontal rule, code fence, or table — since that isn't part of the list.
/// </summary>
public class NestedReorderCarryTests
{
    private static MarkdownEditor At(string text, int line = 0, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    [AvaloniaFact]
    public void SameKindCheckboxChild_IsCarried()
    {
        var e = At("- [ ] a\n  - [ ] a1\n- [ ] b"); // checkbox parent, checkbox child
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - [ ] a1", e.Text);
    }

    [AvaloniaFact]
    public void SameKindBulletSubtree_StillReorders()
    {
        var e = At("- a\n  - a1\n    - a1a\n- b"); // all bullets
        e.MoveLinesForTest(1);
        Assert.Equal("- b\n- a\n  - a1\n    - a1a", e.Text);
    }

    [AvaloniaFact]
    public void DifferentKindNestedBullet_IsCarried()
    {
        // A checkbox parent with a plain-bullet child: the bullet is still a nested list item, so it travels.
        var e = At("- [ ] a\n  - detail\n- [ ] b"); // caret on "a"
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - detail", e.Text);
    }

    [AvaloniaFact]
    public void NumberedChildUnderABullet_IsCarried()
    {
        var e = At("- a\n  1. x\n- b"); // bullet parent, numbered child — different kind, still carried
        e.MoveLinesForTest(1);
        Assert.Equal("- b\n- a\n  1. x", e.Text);
    }

    [AvaloniaFact]
    public void PlainContinuationLine_IsCarried()
    {
        var e = At("- a\n  more text\n- b");
        e.MoveLinesForTest(1);
        Assert.Equal("- b\n- a\n  more text", e.Text);
    }

    [AvaloniaFact]
    public void NestedBlockquote_IsNotCarried_SoTheMoveIsRefused()
    {
        // A blockquote nested under the item isn't part of the list, so it isn't dragged; moving the item
        // would strand it, so the move is a no-op (reorder from the other side instead).
        var e = At("- a\n  > quote\n- b"); // caret on "a"
        e.MoveLinesForTest(1);
        Assert.Equal("- a\n  > quote\n- b", e.Text); // unchanged
    }

    [AvaloniaFact]
    public void NestedHeading_IsNotCarried()
    {
        var e = At("- a\n  # heading\n- b");
        e.MoveLinesForTest(1);
        Assert.Equal("- a\n  # heading\n- b", e.Text); // unchanged (the heading isn't part of the list)
    }

    [AvaloniaFact]
    public void TheOtherSibling_CanStillBeMoved_CarryingTheWholeSubtree()
    {
        // Moving "b" up past "a" carries a's nested blockquote with a (a swap moves the whole subtree).
        var e = At("- a\n  > quote\n- b", line: 2); // caret on "b"
        e.MoveLinesForTest(-1);
        Assert.Equal("- b\n- a\n  > quote", e.Text);
    }

    [AvaloniaFact]
    public void NestedCheckboxesUnderACheckbox_AreCarried()
    {
        var e = At("- [ ] a\n  - [ ] a1\n  - [ ] a2\n- [ ] b");
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - [ ] a1\n  - [ ] a2", e.Text);
    }
}
