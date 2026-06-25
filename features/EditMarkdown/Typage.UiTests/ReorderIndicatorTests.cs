using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The Alt+Shift line reorder lights a drop indicator: faint dotted lines bracket the block being moved.
/// These cover the state that drives the rendering (which line range is bracketed, and that it's active).
/// </summary>
public class ReorderIndicatorTests
{
    private static MarkdownEditor At(string text, int line = 0, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    [AvaloniaFact]
    public void MovingALine_BracketsItsNewPosition()
    {
        var e = At("a\nb\nc"); // caret on "a"
        e.MoveLinesForTest(1);
        var (active, start, end) = e.ReorderIndicatorForTest();
        Assert.True(active);
        Assert.Equal(1, start); // "a" is now line 1
        Assert.Equal(1, end);
    }

    [AvaloniaFact]
    public void MovingASubtree_BracketsTheWholeMovedBlock()
    {
        var e = At("- a\n  - a1\n- b"); // caret on parent "a"; its child rides along
        e.MoveLinesForTest(1);
        // After the move: "- b\n- a\n  - a1" — the moved block spans lines 1-2.
        var (active, start, end) = e.ReorderIndicatorForTest();
        Assert.True(active);
        Assert.Equal(1, start);
        Assert.Equal(2, end);
    }

    [AvaloniaFact]
    public void ABoundaryNoOpMove_DoesNotActivateTheIndicator()
    {
        var e = At("a\nb"); // caret on "a" — can't move up
        e.MoveLinesForTest(-1);
        Assert.False(e.ReorderIndicatorForTest().Active);
    }
}
