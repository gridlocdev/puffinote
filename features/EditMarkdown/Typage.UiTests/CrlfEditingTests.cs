using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// CRLF documents must stay byte-for-byte all-CRLF through editing (golden rule #5). These cover the
/// paragraph-merge and hit-test paths where a line's trailing <c>\r</c> (kept as content by
/// <c>GetLineEnd</c>) previously stranded a bare carriage return or let a click land between <c>\r</c>
/// and <c>\n</c>.
/// </summary>
public class CrlfEditingTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    [AvaloniaFact]
    public void Backspace_MergingCrlfParagraphs_LeavesNoStrayCarriageReturn()
    {
        // "ab" | blank | "cd" separated by CRLF paragraph breaks; Backspace at the start of "cd" merges
        // cleanly to "abcd" — not "ab\rcd" with an orphan carriage return.
        var e = At("ab\r\n\r\ncd", 6); // start of "cd"
        e.BackspaceForTest();
        Assert.Equal("abcd", e.Text);
        Assert.DoesNotContain('\r', e.Text);
        Assert.Equal(2, e.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Delete_MergingCrlfParagraphs_IsSymmetricWithBackspace()
    {
        // Forward-delete at the visible end of "ab" (offset 2, before the \r) merges the same way.
        var e = At("ab\r\n\r\ncd", 2);
        e.DeleteForTest();
        Assert.Equal("abcd", e.Text);
        Assert.DoesNotContain('\r', e.Text);
    }

    [AvaloniaFact]
    public void End_OnACrlfLine_PlacesCaretBeforeTheCarriageReturn()
    {
        // End lands on the visible content end, before the \r — so the symmetric forward-delete guard matches.
        var e = At("ab\r\n\r\ncd", 0);
        e.EndForTest();
        Assert.Equal(2, e.Buffer.Selection.Caret); // before the \r at index 2
    }

    [AvaloniaFact]
    public void Click_BetweenCrlf_InAFlowJoinedParagraph_SnapsToContentSide()
    {
        // "aaa\r\nbbb" is one flow-joined paragraph. A click whose column maps to the \n (offset 4, strictly
        // between the \r and \n) must snap back to offset 3 so a following edit can't split the \r\n.
        var e = new MarkdownEditor { Text = "aaa\r\nbbb" };
        var visual = e.BuildVisualLineForTest(0);
        var xAtNewline = visual.ColumnToX(4); // block-relative column 4 == source offset 4 (the \n)
        var offset = e.OffsetAtForTest(0, xAtNewline);
        Assert.NotEqual(4, offset);          // never strictly between \r and \n
        Assert.Equal(3, offset);             // snapped to the content side (the \r)
    }
}
