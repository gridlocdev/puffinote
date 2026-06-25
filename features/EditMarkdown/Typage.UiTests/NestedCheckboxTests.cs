using System.Linq;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Nested task checkboxes work like nested unordered/ordered lists (indent = nesting), and the
/// Alt+Shift+Up/Down reorder moves an item together with its sub-items, swapping whole sibling subtrees.
/// </summary>
public class NestedCheckboxTests
{
    private static MarkdownEditor At(string text, int line = 0, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    private static DecorationKind? DecorationOn(MarkdownEditor e, int line) =>
        e.BuildVisualLineForTest(line).Runs
            .Where(r => r.Kind == VisualRunKind.Decoration)
            .Select(r => (DecorationKind?)r.Decoration)
            .FirstOrDefault();

    private static double TextX(MarkdownEditor e, int line, string contains) =>
        e.BuildVisualLineForTest(line).Runs.First(r => r.Kind == VisualRunKind.Text && r.Text.Contains(contains)).X;

    // ----- rendering -----

    [AvaloniaFact]
    public void NestedTask_RendersACheckbox_IndentedPastTheParent()
    {
        var e = new MarkdownEditor { Text = "- [ ] top\n  - [ ] sub" };
        Assert.Equal(DecorationKind.CheckboxUnchecked, DecorationOn(e, 0));
        Assert.Equal(DecorationKind.CheckboxUnchecked, DecorationOn(e, 1)); // the nested item also shows a checkbox
        Assert.True(TextX(e, 1, "sub") > TextX(e, 0, "top"), "the nested task's text should hang further right");
    }

    [AvaloniaFact]
    public void NestedTask_IndentsLikeANestedBullet()
    {
        // A nested task and a nested bullet at the same indent put their content at the same column offset
        // relative to their own gutter — i.e. nesting is the leading indentation, same mechanism for both.
        var task = new MarkdownEditor { Text = "  - [ ] x" };
        var bullet = new MarkdownEditor { Text = "  - x" };
        Assert.Equal(DecorationKind.CheckboxUnchecked, DecorationOn(task, 0));
        Assert.Equal(DecorationKind.Bullet, DecorationOn(bullet, 0));
    }

    // ----- subtree reorder -----

    [AvaloniaFact]
    public void MoveDown_CarriesSubItemsWithTheParent()
    {
        var e = At("- [ ] a\n  - [ ] a1\n- [ ] b"); // caret on the parent "a"
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - [ ] a1", e.Text); // a + a1 moved below b as a unit
    }

    [AvaloniaFact]
    public void MoveUp_SwapsWholeSiblingSubtrees()
    {
        var e = At("- [ ] a\n  - [ ] a1\n- [ ] b", line: 2); // caret on "b"
        e.MoveLinesForTest(-1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - [ ] a1", e.Text); // "b" jumps over the entire "a" subtree
    }

    [AvaloniaFact]
    public void SubItems_ReorderAmongThemselves()
    {
        var e = At("- [ ] a\n  - [ ] a1\n  - [ ] a2", line: 1); // caret on the first child
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] a\n  - [ ] a2\n  - [ ] a1", e.Text);
    }

    [AvaloniaFact]
    public void IndividualNestedCheckbox_ReordersAmongSiblings_PreservingItsState()
    {
        var e = At("- [ ] a\n  - [ ] a1\n  - [x] a2\n  - [ ] a3", line: 2); // caret on the middle child (checked)
        e.MoveLinesForTest(-1);
        Assert.Equal("- [ ] a\n  - [x] a2\n  - [ ] a1\n  - [ ] a3", e.Text); // a2 moved up, still checked
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] a\n  - [ ] a1\n  - [x] a2\n  - [ ] a3", e.Text); // and back down again
    }

    [AvaloniaFact]
    public void IndividualNestedChild_CarriesItsOwnSubtree_WhenItHasOne()
    {
        var e = At("- [ ] a\n  - [ ] a1\n    - [ ] a1x\n  - [ ] a2", line: 1); // caret on a1, which has a child
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] a\n  - [ ] a2\n  - [ ] a1\n    - [ ] a1x", e.Text); // a1 + a1x swap with a2 as a unit
    }

    [AvaloniaFact]
    public void FirstChild_CannotMoveAboveItsParent()
    {
        var e = At("- [ ] a\n  - [ ] a1", line: 1); // the only child
        e.MoveLinesForTest(-1);
        Assert.Equal("- [ ] a\n  - [ ] a1", e.Text); // no-op: no previous sibling at the child's level
    }

    [AvaloniaFact]
    public void DeepSubtree_MovesAllDescendants()
    {
        var e = At("- [ ] a\n  - [ ] a1\n    - [ ] a1a\n- [ ] b"); // caret on "a"
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] b\n- [ ] a\n  - [ ] a1\n    - [ ] a1a", e.Text); // the grandchild comes along too
    }

    [AvaloniaFact]
    public void NestedUnorderedList_ReordersAsASubtree_Too()
    {
        var e = At("- a\n  - a1\n- b"); // generality: same behaviour for plain nested bullets
        e.MoveLinesForTest(1);
        Assert.Equal("- b\n- a\n  - a1", e.Text);
    }

    [AvaloniaFact]
    public void CaretRidesWithTheMovedItem()
    {
        var e = At("- [ ] a\n  - [ ] a1\n- [ ] b", col: 6); // caret inside "a"'s text
        e.MoveLinesForTest(1);
        // After the move the caret is still inside the "a" line (now the second line).
        Assert.Equal(1, e.Buffer.Document.GetPosition(e.Buffer.Selection.Caret).Line);
    }
}
