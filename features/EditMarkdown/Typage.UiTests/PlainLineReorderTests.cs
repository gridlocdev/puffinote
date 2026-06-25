using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// A line that isn't a list item (plain text, a heading, a blockquote) reorders like any other single line:
/// Alt+Shift+Up/Down swaps it with the immediately adjacent line, regardless of that neighbour's indentation.
/// It does NOT use the list-item subtree logic (which would make it skip over an indented neighbour).
/// </summary>
public class PlainLineReorderTests
{
    private static MarkdownEditor At(string text, int line, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    [AvaloniaFact]
    public void MoveUp_OverIndentedText_SwapsWithIt_NotAboveTheBullet()
    {
        var e = At("- bullet\n  indented text\nthird line", 2); // the reported bug
        e.MoveLinesForTest(-1);
        Assert.Equal("- bullet\nthird line\n  indented text", e.Text);
    }

    [AvaloniaFact]
    public void MoveDown_OverIndentedText_SwapsWithIt()
    {
        var e = At("third line\n  indented text\n- bullet", 0);
        e.MoveLinesForTest(1);
        Assert.Equal("  indented text\nthird line\n- bullet", e.Text);
    }

    [AvaloniaFact]
    public void MoveUp_CaretFollowsTheLine()
    {
        var e = At("- bullet\n  indented text\nthird line", 2, col: 2); // caret inside "third line"
        e.MoveLinesForTest(-1);
        Assert.Equal(1, e.Buffer.Document.GetPosition(e.Buffer.Selection.Caret).Line); // now line 1
    }

    [AvaloniaFact]
    public void MoveUp_AtTop_IsANoOp()
    {
        var e = At("first\n- bullet", 0);
        e.MoveLinesForTest(-1);
        Assert.Equal("first\n- bullet", e.Text);
    }

    [AvaloniaFact]
    public void MoveDown_AtBottom_IsANoOp()
    {
        var e = At("- bullet\nlast", 1);
        e.MoveLinesForTest(1);
        Assert.Equal("- bullet\nlast", e.Text);
    }

    [AvaloniaFact]
    public void AHeading_MovesAsAPlainSingleLine()
    {
        var e = At("# title\n  indented\nbody", 0);
        e.MoveLinesForTest(1);
        Assert.Equal("  indented\n# title\nbody", e.Text);
    }

    [AvaloniaFact]
    public void MovingAPlainLineIntoANumberedList_RenumbersTheSource()
    {
        // The plain line is moved down into the list, splitting it; the second part restarts at 1 in source.
        var e = At("plain\n1. a\n2. b", 0);
        e.MoveLinesForTest(1);
        Assert.Equal("1. a\nplain\n1. b", e.Text); // a alone (1), then a fresh list b (1)
    }

    [AvaloniaFact]
    public void MovingAPlainLineOutOfANumberedList_RejoinsTheNumbering()
    {
        var e = At("1. a\nplain\n2. b", 1); // plain between the two items
        e.MoveLinesForTest(1);             // move plain below "2. b"
        Assert.Equal("1. a\n2. b\nplain", e.Text); // a and b are now contiguous -> 1, 2
    }
}
