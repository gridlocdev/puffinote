using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Alt+Shift reorder carries a nested item's sub-items only when they're the SAME list kind (bulleted /
/// numbered / checkbox) as the moved line. A different-kind nested block is not dragged along; moving an
/// item that has one is refused (the other sibling can still be moved to reorder).
/// </summary>
public class SameKindReorderTests
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
    public void DifferentKindNestedList_IsNotCarried_SoTheMoveIsRefused()
    {
        // The checkbox parent has a bullet child (a different kind). Moving the parent down would strand
        // the bullet block, so it's a no-op.
        var e = At("- [ ] a\n  - detail\n- [ ] b"); // caret on "a"
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] a\n  - detail\n- [ ] b", e.Text); // unchanged
    }

    [AvaloniaFact]
    public void TheOtherSibling_CanStillBeMoved_CarryingTheWholeSubtree()
    {
        // Moving "b" up past "a" works and carries a's different-kind child with a (a swap moves the whole
        // subtree), so the reorder is still achievable from the other side.
        var e = At("- [ ] a\n  - detail\n- [ ] b", line: 2); // caret on "b"
        e.MoveLinesForTest(-1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - detail", e.Text);
    }

    [AvaloniaFact]
    public void NumberedChildUnderABullet_IsNotCarried()
    {
        var e = At("- a\n  1. x\n- b"); // bullet parent, numbered child
        e.MoveLinesForTest(1);
        Assert.Equal("- a\n  1. x\n- b", e.Text); // no-op
    }

    [AvaloniaFact]
    public void PlainContinuationLine_IsStillCarried()
    {
        // A plain (non-list) indented continuation line is part of the item and travels with it.
        var e = At("- a\n  more text\n- b");
        e.MoveLinesForTest(1);
        Assert.Equal("- b\n- a\n  more text", e.Text);
    }

    [AvaloniaFact]
    public void NestedCheckboxesUnderACheckbox_AreCarried()
    {
        var e = At("- [ ] a\n  - [ ] a1\n  - [ ] a2\n- [ ] b");
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - [ ] a1\n  - [ ] a2", e.Text);
    }
}
