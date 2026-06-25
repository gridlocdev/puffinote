using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Reordering numbered list items renumbers the SOURCE to match the preview (which always shows a list 1, 2,
/// 3… per run). So switching to source view after an Alt+Shift move shows the same order as the preview, not
/// the stale literal digits. The whole reorder + renumber is one undo step.
/// </summary>
public class OrderedReorderTests
{
    private static MarkdownEditor At(string text, int line = 0, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    [AvaloniaFact]
    public void MoveUp_RenumbersTheSourceSequentially()
    {
        var e = At("1. a\n2. b\n3. c", line: 2); // caret on "c"
        e.MoveLinesForTest(-1);
        Assert.Equal("1. a\n2. c\n3. b", e.Text); // c moved up; numbers stay 1,2,3 in the source
    }

    [AvaloniaFact]
    public void MoveDown_RenumbersTheSourceSequentially()
    {
        var e = At("1. a\n2. b\n3. c", line: 0); // caret on "a"
        e.MoveLinesForTest(1);
        Assert.Equal("1. b\n2. a\n3. c", e.Text);
    }

    [AvaloniaFact]
    public void SubItems_RenumberWithinTheirSublist()
    {
        var e = At("1. a\n   1. a1\n   2. a2\n2. b", line: 1); // caret on the first sub-item
        e.MoveLinesForTest(1);
        Assert.Equal("1. a\n   1. a2\n   2. a1\n2. b", e.Text); // sublist renumbered, parents untouched
    }

    [AvaloniaFact]
    public void MovingAParent_CarriesAndRenumbersItsSublist()
    {
        var e = At("1. a\n   1. a1\n   2. a2\n2. b", line: 0); // caret on parent "a"
        e.MoveLinesForTest(1);
        Assert.Equal("1. b\n2. a\n   1. a1\n   2. a2", e.Text); // a + sublist moved below b; all renumbered
    }

    [AvaloniaFact]
    public void RenumberAcrossADigitWidthChange_KeepsTheCaretOnTheItem()
    {
        // The source starts at 9/10 but the preview shows 1/2; after the swap the source is renumbered to
        // 1/2, so the moved item's number narrows 9→… and widens across the swap. The caret rides along.
        var e = At("9. i\n10. j", line: 0, col: 4); // caret at the end of "9. i"
        e.MoveLinesForTest(1);
        Assert.Equal("1. j\n2. i", e.Text);
        Assert.Equal(1, e.Buffer.Document.GetPosition(e.Buffer.Selection.Caret).Line); // caret followed "i" down
        Assert.True(e.Buffer.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void TheReorderAndRenumber_AreASingleUndoStep()
    {
        var e = At("1. a\n2. b\n3. c", line: 2);
        e.MoveLinesForTest(-1);
        Assert.Equal("1. a\n2. c\n3. b", e.Text);
        e.Buffer.Undo();
        Assert.Equal("1. a\n2. b\n3. c", e.Text); // one undo restores everything
    }
}
