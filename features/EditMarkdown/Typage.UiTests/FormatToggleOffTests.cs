using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
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
    public void Italic_OnABoldWord_AddsItalicThenDropsBackToBold()
    {
        // Bold and italic are independent layers over the same delimiter; italic on bold → bold-italic,
        // and italic again removes only the italic layer, leaving bold.
        var e = At("**word**", caret: 4); // caret inside the bold "word"
        e.ToggleItalic();
        Assert.Equal("***word***", e.Text);

        e.ToggleItalic();
        Assert.Equal("**word**", e.Text);
    }

    [AvaloniaFact]
    public void Bold_OnBoldItalicWord_LeavesItalic()
    {
        var e = At("***word***", caret: 5); // caret inside bold-italic "word"
        e.ToggleBold();
        Assert.Equal("*word*", e.Text);
    }

    [AvaloniaFact]
    public void Bold_OnAnItalicWord_ProducesBoldItalic()
    {
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

    // ----- toggling off from anywhere inside the run (not just its exact content) -----
    //
    // In the live-preview view the ~~ markers are invisible, so every one of these *looks* to the reader like
    // "the caret / selection is in struck text". Each therefore has to turn the strike off. Requiring the
    // target to be exactly the content is what used to send them all down the wrap path instead, stacking
    // another ~~ pair onto text that was already struck, on every press.

    [AvaloniaFact]
    public void Strikethrough_WithTheCaretInsideAStruckPhrase_RemovesTheWholeStrike()
    {
        var e = At("~~this is done~~", caret: 8); // caret in the middle word
        e.ToggleStrikethrough();
        Assert.Equal("this is done", e.Text);
    }

    [AvaloniaFact]
    public void Strikethrough_WithASelectionThatSweptTheHiddenMarkersIn_RemovesTheStrike()
    {
        // What a mouse drag across the struck word produces: the concealed markers are zero-width, so the
        // selection ends can land outside them.
        var e = new MarkdownEditor { Text = "~~done~~" };
        e.Buffer.Selection = new Selection(0, 8);
        e.ToggleStrikethrough();
        Assert.Equal("done", e.Text);
    }

    [AvaloniaFact]
    public void Strikethrough_OnASelectAllOfAStruckLine_RemovesTheStrike()
    {
        var e = new MarkdownEditor { Text = "~~done~~" };
        e.SelectAll();
        e.ToggleStrikethrough();
        Assert.Equal("done", e.Text);
    }

    [AvaloniaFact]
    public void Strikethrough_WithTheCaretAtTheStruckWordsVisibleEdge_RemovesTheStrike()
    {
        // Offset 0 is *before* the hidden "~~" — visually the caret is right at the start of the word.
        var e = At("~~done~~", caret: 0);
        e.ToggleStrikethrough();
        Assert.Equal("done", e.Text);
    }

    [AvaloniaFact]
    public void Strikethrough_NeverStacksASecondMarkerPair()
    {
        // The reported bug: pressing it again on already-struck text kept adding tildes to each end.
        var e = At("done", caret: 2);
        e.ToggleStrikethrough();
        e.ToggleStrikethrough();
        e.ToggleStrikethrough();
        Assert.Equal("~~done~~", e.Text);
        Assert.DoesNotContain("~~~", e.Text);
    }

    [AvaloniaFact]
    public void Strikethrough_InsideBold_TogglesTheNestedStrike()
    {
        // The tokenizer emits emphasis content flat, so the "~~" here is part of the Strong content's text and
        // has no token of its own — the run is found by scanning one level in, exactly as the renderer does to
        // draw the strike.
        var e = At("**~~done~~**", caret: 6);
        e.ToggleStrikethrough();
        Assert.Equal("**done**", e.Text);
    }

    [AvaloniaFact]
    public void Bold_WithTheCaretInsideABoldPhrase_UnboldsTheWholePhrase()
    {
        var e = At("**this is done**", caret: 8);
        e.ToggleBold();
        Assert.Equal("this is done", e.Text);
    }

    [AvaloniaFact]
    public void Italic_WithTheCaretInsideABoldPhrase_LayersOverTheWholePhrase()
    {
        // The layer applies to the run the caret is in, not just the word under it.
        var e = At("**this is done**", caret: 8);
        e.ToggleItalic();
        Assert.Equal("***this is done***", e.Text);
    }

    // ----- wrapping over text that already contains a run of the same format -----

    [AvaloniaFact]
    public void Strikethrough_OverASpanContainingAStruckWord_StripsTheInnerMarkers()
    {
        // "~~a ~~b~~ c~~" isn't a nested strike, it's a broken one — so the inner pair goes.
        var e = new MarkdownEditor { Text = "a ~~b~~ c" };
        e.SelectAll();
        e.ToggleStrikethrough();
        Assert.Equal("~~a b c~~", e.Text);
    }

    [AvaloniaFact]
    public void Bold_OverASpanContainingABoldWord_StripsOnlyTheBoldLayer()
    {
        var e = new MarkdownEditor { Text = "a **b** c" };
        e.SelectAll();
        e.ToggleBold();
        Assert.Equal("**a b c**", e.Text);
    }

    [AvaloniaFact]
    public void Bold_OverASpanContainingAnItalicWord_KeepsTheItalic()
    {
        // Bold and italic are different layers of the same delimiter, so an italic inside a bold is valid —
        // only the layer being added is taken off the inner run.
        var e = new MarkdownEditor { Text = "a *b* c" };
        e.SelectAll();
        e.ToggleBold();
        Assert.Equal("**a *b* c**", e.Text);
    }

    [AvaloniaFact]
    public void TheToolbarLightsTheToolThatWouldTurnTheFormatOff()
    {
        // The lit state and the toggle read the same run-finder, so they can't disagree about what the caret
        // is inside.
        Assert.True(At("~~this is done~~", caret: 8).IsStrikethroughActive);
        Assert.True(At("**this is done**", caret: 8).IsBoldActive);
        Assert.True(At("**~~done~~**", caret: 6).IsStrikethroughActive);
        Assert.False(At("plain text", caret: 3).IsStrikethroughActive);
    }
}
