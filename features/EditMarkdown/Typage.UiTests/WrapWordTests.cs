using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Bold/italic/strikethrough/inline-code with no selection wrap the word the caret is inside (rather than
/// dropping an empty marker pair). On whitespace they still insert an empty pair to type into.
/// </summary>
public class WrapWordTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    [AvaloniaFact]
    public void Bold_WithCaretInsideAWord_WrapsTheWord()
    {
        var e = At("hello world", caret: 8); // caret inside "world"
        e.ToggleBold();
        Assert.Equal("hello **world**", e.Text);
    }

    [AvaloniaFact]
    public void Italic_AtTheStartOfAWord_WrapsIt()
    {
        var e = At("hello world", caret: 6); // caret right before "world"
        e.ToggleItalic();
        Assert.Equal("hello *world*", e.Text);
    }

    [AvaloniaFact]
    public void Bold_AtTheEndOfAWord_WrapsIt()
    {
        var e = At("hello", caret: 5); // caret right after the last letter
        e.ToggleBold();
        Assert.Equal("**hello**", e.Text);
    }

    [AvaloniaFact]
    public void Strikethrough_WrapsTheWordUnderTheCaret()
    {
        var e = At("done", caret: 2);
        e.ToggleStrikethrough();
        Assert.Equal("~~done~~", e.Text);
    }

    [AvaloniaFact]
    public void Bold_OnWhitespace_InsertsAnEmptyPair()
    {
        var e = At("a  b", caret: 2); // caret between the two spaces, not touching a word
        e.ToggleBold();
        Assert.Equal("a **** b", e.Text);
        Assert.Equal(4, e.Buffer.Selection.Caret); // caret sits between the markers
    }

    [AvaloniaFact]
    public void Bold_LeavesTheWrappedWordSelected()
    {
        var e = At("word", caret: 2);
        e.ToggleBold();
        // The inner "word" (offsets 2..6 of "**word**") is selected.
        Assert.Equal("word", e.Text.Substring(e.Buffer.Selection.Start, e.Buffer.Selection.Length));
    }

    [AvaloniaFact]
    public void Bold_DoesNotReachAcrossASpaceOrLine()
    {
        var e = At("alpha bravo", caret: 3); // inside "alpha"
        e.ToggleBold();
        Assert.Equal("**alpha** bravo", e.Text);
    }
}
