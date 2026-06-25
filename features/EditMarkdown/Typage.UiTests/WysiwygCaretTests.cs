using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Full-WYSIWYG caret behaviour (PRD §7.4, revised): the caret glides over hidden marker punctuation
/// atomically (arrows never stop strictly inside it), and backspace/delete act on the visible grapheme
/// rather than an invisible <c>*</c>, so the document edits like a normal rich-text editor.
/// </summary>
public class WysiwygCaretTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var editor = new MarkdownEditor { Text = text };
        editor.Buffer.Selection = Selection.At(caret);
        return editor;
    }

    [AvaloniaFact]
    public void RightArrow_SkipsHiddenOpeningMarker()
    {
        // "**bold**": cols 0-1 = opening **, 2-5 = bold, 6-7 = closing **.
        var editor = At("**bold**", caret: 0);
        editor.VisualStepForTest(1); // one step right from the very start
        // Lands on the first visible glyph 'b' at column 2, not strictly inside the hidden "**".
        Assert.Equal(2, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void RightArrow_GlidesThroughStackedMarkers_InOneStep()
    {
        // "***~~bold~~***": cols 0-2 ***, 3-4 ~~, 5-8 bold, 9-10 ~~, 11-13 ***. The junction at column 3
        // (between *** and ~~) is zero-width and visually identical to the start of "bold", so a single
        // Right from the start lands directly on 'b' (col 5) instead of stopping at the dead junction.
        var editor = At("***~~bold~~***", caret: 0);
        editor.VisualStepForTest(1);
        Assert.Equal(5, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void RightArrow_GlidesPastALinksTrailingMarkers_ToLineEnd()
    {
        // "[a](b)": after the visible 'a' (col 2), the "](", the concealed URL, and ")" are all hidden and
        // zero-width — one Right glides past them to the line end rather than stopping at each junction.
        var editor = At("[a](b)", caret: 2);
        editor.VisualStepForTest(1);
        Assert.Equal(6, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void LeftArrow_SkipsHiddenClosingMarker()
    {
        var editor = At("**bold**", caret: 8); // end of line, just past the closing **
        editor.VisualStepForTest(-1);
        // Steps back over the hidden closing "**" and onto the last visible glyph boundary (after 'd').
        Assert.Equal(6, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Backspace_AtEndOfBold_RemovesALetter_NotAMarker()
    {
        var editor = At("**bold**", caret: 8); // visual end of the word
        editor.BackspaceForTest();
        Assert.Equal("**bol**", editor.Text); // the 'd' went; the bold markers are intact
    }

    [AvaloniaFact]
    public void Delete_AtStartOfBold_RemovesALetter_NotAMarker()
    {
        var editor = At("**bold**", caret: 0); // visual start of the word
        editor.DeleteForTest();
        Assert.Equal("**old**", editor.Text); // the 'b' went; the markers are intact
    }

    [AvaloniaFact]
    public void Backspace_AtListContentStart_RemovesTheMarkerPrefix()
    {
        var editor = At("- item", caret: 2); // caret at content start, after the hidden "- "
        editor.BackspaceForTest();
        Assert.Equal("item", editor.Text); // the whole list marker is removed (un-lists the line)
    }

    [AvaloniaFact]
    public void Backspace_AtNestedBulletContentStart_RemovesTheMarker_KeepingIndent()
    {
        var editor = At("- a\n  - b", caret: 8); // caret right after the nested "- " marker, on "b"
        editor.BackspaceForTest();
        Assert.Equal("- a\n  b", editor.Text); // the bullet is deleted; the indentation stays
        Assert.Equal(6, editor.Buffer.Selection.Caret); // caret at the (kept) indentation end
    }

    [AvaloniaFact]
    public void Backspace_AtNestedCheckboxContentStart_RemovesTheCheckbox()
    {
        var editor = At("- a\n  - [ ] c", caret: 12); // caret after the nested "- [ ] "
        editor.BackspaceForTest();
        Assert.Equal("- a\n  c", editor.Text); // the whole checkbox marker is removed, indentation kept
    }

    [AvaloniaFact]
    public void Backspace_AtNestedNumberContentStart_RemovesTheNumber()
    {
        var editor = At("- a\n  1. d", caret: 9); // caret after the nested "1. "
        editor.BackspaceForTest();
        Assert.Equal("- a\n  d", editor.Text);
    }

    [AvaloniaFact]
    public void Backspace_OnIndentedPlainText_StillDeletesAnIndentSpace()
    {
        // No marker to strip here: a leading-whitespace plain line backspaces a single indent char.
        var editor = At("- a\n    text", caret: 8); // caret inside the indentation, before "text"
        editor.BackspaceForTest();
        Assert.Equal("- a\n   text", editor.Text); // one space removed, not a no-op
    }

    [AvaloniaFact]
    public void Backspace_OnAWhitespaceOnlyLine_ClearsTheLine()
    {
        var editor = At("a\n    ", caret: 6); // caret at the end of a spaces-only line
        editor.BackspaceForTest();
        Assert.Equal("a\n", editor.Text);             // the whole line's whitespace is gone
        Assert.Equal(2, editor.Buffer.Selection.Caret); // caret at the (now empty) line start
    }

    [AvaloniaFact]
    public void Backspace_OnATabOnlyLine_ClearsTheLine()
    {
        var editor = At("a\n\t\t", caret: 4); // caret at the end of a tabs-only line
        editor.BackspaceForTest();
        Assert.Equal("a\n", editor.Text);
        Assert.Equal(2, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Backspace_MidWhitespaceLine_StillClearsTheWholeLine()
    {
        var editor = At("a\n    ", caret: 4); // caret in the middle of the spaces
        editor.BackspaceForTest();
        Assert.Equal("a\n", editor.Text);
        Assert.Equal(2, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Backspace_OnAWhitespaceLine_PreservesCrlf()
    {
        // CRLF must stay byte-for-byte: only the spaces go, the line's \r\n is kept.
        var editor = At("x\r\n   \r\ny", caret: 6); // caret after the three spaces, before the \r
        editor.BackspaceForTest();
        Assert.Equal("x\r\n\r\ny", editor.Text);
    }
}
