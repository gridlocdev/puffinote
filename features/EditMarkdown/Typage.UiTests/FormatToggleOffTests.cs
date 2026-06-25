using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Applying a text format (bold/italic/strikethrough/highlight/inline-code) a second time to text that is
/// already in that format removes it — the toggle unwraps the run it expanded to (word under the caret or
/// the selection), rather than nesting another marker pair.
/// </summary>
public class FormatToggleOffTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    [AvaloniaFact]
    public void Bold_AppliedTwiceToAWord_RemovesIt()
    {
        var e = At("hello world", caret: 8);
        e.ToggleBold();
        Assert.Equal("hello **world**", e.Text);

        // Caret is still inside the (now bold) word; a second press unwraps it.
        e.ToggleBold();
        Assert.Equal("hello world", e.Text);
        Assert.Equal("world", e.Text.Substring(e.Buffer.Selection.Start, e.Buffer.Selection.Length));
    }

    [AvaloniaFact]
    public void Italic_AppliedTwiceToASelection_RemovesIt()
    {
        var e = new MarkdownEditor { Text = "hello world" };
        e.Buffer.Selection = new Selection(6, 11); // "world"
        e.ToggleItalic();
        Assert.Equal("hello *world*", e.Text);

        // The wrap leaves the inner content selected; toggling again strips it.
        e.ToggleItalic();
        Assert.Equal("hello world", e.Text);
    }

    [AvaloniaFact]
    public void Strikethrough_RoundTrips()
    {
        var e = At("done", caret: 2);
        e.ToggleStrikethrough();
        Assert.Equal("~~done~~", e.Text);
        e.ToggleStrikethrough();
        Assert.Equal("done", e.Text);
    }

    [AvaloniaFact]
    public void Highlight_RoundTrips()
    {
        var e = At("note", caret: 2);
        e.ToggleHighlight();
        Assert.Equal("==note==", e.Text);
        e.ToggleHighlight();
        Assert.Equal("note", e.Text);
    }

    [AvaloniaFact]
    public void InlineCode_RoundTrips()
    {
        var e = At("code", caret: 2);
        e.ToggleInlineCode();
        Assert.Equal("`code`", e.Text);
        e.ToggleInlineCode();
        Assert.Equal("code", e.Text);
    }

    [AvaloniaFact]
    public void Bold_OnAlreadyItalicWord_NestsRatherThanUnwrapping()
    {
        // A different format than the one present doesn't toggle off — bold over an italic word adds bold.
        var e = At("*word*", caret: 3); // caret inside the italic "word"
        e.ToggleBold();
        Assert.Equal("***word***", e.Text);
    }

    [AvaloniaFact]
    public void Bold_UnwrapIsASingleUndoStep()
    {
        var e = At("word", caret: 2);
        e.ToggleBold();
        Assert.Equal("**word**", e.Text);
        e.ToggleBold();
        Assert.Equal("word", e.Text);

        // The unwrap is one edit: a single undo restores the bold.
        e.Buffer.Undo();
        Assert.Equal("**word**", e.Text);
    }
}
