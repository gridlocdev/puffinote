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
}
