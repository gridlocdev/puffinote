using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Alt+Shift reorder carries a moved list item together only with its nested sub-items of the <b>same kind</b>
/// (bulleted / numbered / checkbox) as the line the move started on. Plain continuation text and nested items
/// of a different kind don't apply to the list item, so they're left behind: the item hops over them rather
/// than dragging them along (and the move is never refused on their account).
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
    public void DifferentKindNestedBullet_IsLeftBehind()
    {
        // A checkbox parent with a plain-bullet child: the bullet is a different kind, so it isn't carried —
        // the checkbox hops over it, leaving the bullet where it was.
        var e = At("- [ ] a\n  - detail\n- [ ] b"); // caret on "a"
        e.MoveLinesForTest(1);
        Assert.Equal("  - detail\n- [ ] a\n- [ ] b", e.Text);
    }

    [AvaloniaFact]
    public void NumberedChildUnderABullet_IsLeftBehind()
    {
        var e = At("- a\n  1. x\n- b"); // bullet parent, numbered child — different kind, not carried
        e.MoveLinesForTest(1);
        Assert.Equal("  1. x\n- a\n- b", e.Text);
    }

    [AvaloniaFact]
    public void PlainContinuationLine_IsLeftBehind()
    {
        // The user's canonical case: moving "- a" down moves JUST the "- a" line — below "more text",
        // above "- b" — leaving the continuation text in place.
        var e = At("- a\n  more text\n- b");
        e.MoveLinesForTest(1);
        Assert.Equal("  more text\n- a\n- b", e.Text);
    }

    [AvaloniaFact]
    public void NestedBlockquote_IsLeftBehind_AndTheMoveStillHappens()
    {
        // A blockquote nested under the item isn't a same-kind list item, so it isn't dragged; the move now
        // proceeds (no longer refused) and leaves the quote behind.
        var e = At("- a\n  > quote\n- b"); // caret on "a"
        e.MoveLinesForTest(1);
        Assert.Equal("  > quote\n- a\n- b", e.Text);
    }

    [AvaloniaFact]
    public void NestedHeading_IsLeftBehind()
    {
        var e = At("- a\n  # heading\n- b");
        e.MoveLinesForTest(1);
        Assert.Equal("  # heading\n- a\n- b", e.Text);
    }

    [AvaloniaFact]
    public void MovingTheParent_CarriesSameKindChildren_ButNotTheirContinuation()
    {
        // Moving the highest parent carries the same-kind nested item ("- b") but leaves the plain
        // continuation text under that nested item behind.
        var e = At("- a\n  - b\n  more text\n- c"); // caret on "a"
        e.MoveLinesForTest(1);
        Assert.Equal("  more text\n- a\n  - b\n- c", e.Text);
    }

    [AvaloniaFact]
    public void NestedCheckboxesUnderACheckbox_AreCarried()
    {
        var e = At("- [ ] a\n  - [ ] a1\n  - [ ] a2\n- [ ] b");
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - [ ] a1\n  - [ ] a2", e.Text);
    }
}
