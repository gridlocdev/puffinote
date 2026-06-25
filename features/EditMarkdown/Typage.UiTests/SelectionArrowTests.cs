using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// A bare Up/Down arrow with an active range selection collapses to the selection's edge (Up → top,
/// Down → bottom) instead of stepping a visual row from the active caret. So Up right after Select All
/// returns to the top of the document, not the second-to-last line.
/// </summary>
public class SelectionArrowTests
{
    private static int Caret(MarkdownEditor e) => e.Buffer.Selection.Caret;

    [AvaloniaFact]
    public void Up_AfterSelectAll_GoesToTheTopOfTheSelection()
    {
        var e = new MarkdownEditor { Text = "one\ntwo\nthree" };
        e.SelectAll(); // anchor 0, caret at the end
        e.ArrowByLineForTest(-1);
        Assert.Equal(0, Caret(e)); // top of the selection (document start), not one row up from the end
        Assert.True(e.Buffer.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void Down_AfterSelectAll_GoesToTheBottomOfTheSelection()
    {
        var e = new MarkdownEditor { Text = "one\ntwo\nthree" };
        e.SelectAll();
        e.ArrowByLineForTest(1);
        Assert.Equal(e.Buffer.Document.Length, Caret(e)); // bottom of the selection (document end)
        Assert.True(e.Buffer.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void Up_WithAPartialSelection_CollapsesToItsStart()
    {
        var e = new MarkdownEditor { Text = "one\ntwo\nthree" };
        var start = e.Buffer.Document.GetLineStart(1) + 1; // inside "two"
        var end = e.Buffer.Document.GetLineStart(2) + 2;    // inside "three"
        e.Buffer.Selection = new Selection(start, end);
        e.ArrowByLineForTest(-1);
        Assert.Equal(start, Caret(e)); // collapsed to the top edge
    }

    [AvaloniaFact]
    public void Shift_Up_StillExtendsByARow()
    {
        // List items stay on separate rows (plain paragraphs would join into one flowing block).
        var e = new MarkdownEditor { Text = "- one\n- two\n- three" };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.Length); // caret at end, no selection
        e.ArrowByLineForTest(-1, extend: true);
        Assert.False(e.Buffer.Selection.IsEmpty); // extending up grows a selection
    }

    [AvaloniaFact]
    public void Up_WithNoSelection_MovesByARow()
    {
        var e = new MarkdownEditor { Text = "- one\n- two\n- three" };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.Length); // on "three"
        e.ArrowByLineForTest(-1);
        Assert.Equal(1, e.Buffer.Document.GetPosition(Caret(e)).Line); // moved up to "two"
    }
}
