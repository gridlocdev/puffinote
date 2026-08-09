using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Enter in the raw-text view adds one line, not two.
/// <para>
/// Plain Enter writes a Markdown paragraph break — <c>\n\n</c> — so the two halves do not flow back
/// together, and in the two formatted views the blank it leaves is collapsed to regular paragraph spacing,
/// so one press shows one new line. The raw-text view ungroups paragraphs and does not collapse blanks (it
/// draws the source as it is), so the same two newlines showed up as what they literally are: a blank line.
/// One press, two lines.
/// </para>
/// <para>
/// Only the break changes. List continuation, exiting a list, sliding a marked item down — all identical in
/// all three views, and the two formatted views are untouched.
/// </para>
/// </summary>
public class RawTextEnterTests
{
    private static MarkdownEditor At(string text, int caret, MarkdownEditor.ViewMode view)
    {
        var editor = new MarkdownEditor { Text = text, View = view };
        editor.Buffer.Selection = Selection.At(caret);
        return editor;
    }

    [AvaloniaFact]
    public void Enter_in_raw_text_adds_one_newline()
    {
        var editor = At("Hello world", 5, MarkdownEditor.ViewMode.RawText);

        editor.EnterForTest();

        Assert.Equal("Hello\n world", editor.Text);
        Assert.Equal(6, editor.Buffer.Selection.Caret); // straight after the newline it just wrote
    }

    /// <summary>
    /// The other two views keep the paragraph break they need: a single newline there is a soft break the
    /// tokenizer flows back together, which is the "teleporting caret" the double break exists to prevent.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(MarkdownEditor.ViewMode.Rich)]
    [InlineData(MarkdownEditor.ViewMode.Formatted)]
    public void Enter_in_the_formatted_views_still_writes_a_paragraph_break(MarkdownEditor.ViewMode view)
    {
        var editor = At("Hello world", 5, view);

        editor.EnterForTest();

        Assert.Equal("Hello\n\n world", editor.Text);
        Assert.Equal(7, editor.Buffer.Selection.Caret);
    }

    /// <summary>
    /// With a newline already after the caret the formatted views write one and step the caret *over* the
    /// blank they are collapsing. Raw text collapses nothing, so stepping over it would put the caret a
    /// line below the one the press created.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_does_not_skip_a_line_that_raw_text_is_not_collapsing()
    {
        var editor = At("Hello\nworld", 5, MarkdownEditor.ViewMode.RawText); // end of "Hello", newline next

        editor.EnterForTest();

        Assert.Equal("Hello\n\nworld", editor.Text);
        Assert.Equal(6, editor.Buffer.Selection.Caret); // on the new blank line, not below it
    }

    /// <summary>Enter on a blank line or at a line start was already one newline — nothing to change there.</summary>
    [AvaloniaFact]
    public void Enter_at_a_line_start_is_one_newline_in_every_view()
    {
        foreach (var view in new[] { MarkdownEditor.ViewMode.Rich, MarkdownEditor.ViewMode.Formatted, MarkdownEditor.ViewMode.RawText })
        {
            var editor = At("Hello", 0, view);
            editor.EnterForTest();
            Assert.Equal("\nHello", editor.Text);
        }
    }

    /// <summary>Byte-for-byte line endings (golden rule 5): a CRLF file gets a CRLF, not a bare LF.</summary>
    [AvaloniaFact]
    public void Raw_text_writes_the_documents_own_line_ending()
    {
        var editor = At("Hello\r\nworld", 5, MarkdownEditor.ViewMode.RawText);

        editor.EnterForTest();

        Assert.Equal("Hello\r\n\r\nworld", editor.Text);
    }

    /// <summary>
    /// Everything else Enter does is unchanged. A list still continues in the raw view — that is not the
    /// paragraph break, and it was never the thing adding a second line.
    /// </summary>
    [AvaloniaFact]
    public void A_list_still_continues_in_raw_text()
    {
        var editor = At("- milk", 6, MarkdownEditor.ViewMode.RawText);

        editor.EnterForTest();

        Assert.Equal("- milk\n- ", editor.Text);
    }

    /// <summary>Replacing a selection takes the same one newline, rather than the paragraph break.</summary>
    [AvaloniaFact]
    public void Enter_over_a_selection_replaces_it_with_one_newline()
    {
        var editor = new MarkdownEditor { Text = "Hello cruel world", View = MarkdownEditor.ViewMode.RawText };
        editor.Buffer.Selection = new Selection(5, 11); // " cruel", leaving " world" behind it

        editor.EnterForTest();

        Assert.Equal("Hello\n world", editor.Text); // one newline in its place, not a paragraph break
        Assert.Equal(6, editor.Buffer.Selection.Caret);
    }
}
