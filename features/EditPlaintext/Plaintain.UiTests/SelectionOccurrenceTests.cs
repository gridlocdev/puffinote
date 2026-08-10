using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Plaintain.Control.Controls;
using Puffinote.Text.Document;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Select a chunk of text and every other place it appears is tinted, more quietly than the selection
/// itself — "where else is this?", answered without opening Find.
/// <para>
/// The paint has no pixels to assert on in a headless run, so what these hold is the rule: which ranges
/// would be tinted. The drawing then clips those ranges to each visible row through the same arithmetic
/// the selection and the search matches already use.
/// </para>
/// </summary>
public class SelectionOccurrenceTests
{
    private static PlainTextEditor Over(string text, int start, int end)
    {
        var editor = new PlainTextEditor { Text = text };
        editor.Buffer.Selection = new Selection(start, end);
        return editor;
    }

    private static int[] Tinted(PlainTextEditor editor) =>
        [.. editor.OccurrenceHighlightsForTest().Select(m => m.Start)];

    [AvaloniaFact]
    public void The_other_occurrences_of_the_selection_are_tinted_and_the_selected_one_is_not()
    {
        var editor = Over("log log log", 0, 3);

        Assert.Equal([4, 8], Tinted(editor));
    }

    [AvaloniaFact]
    public void Occurrences_are_found_on_every_line_not_only_the_selection_s_own()
    {
        var editor = Over("log\nmsg\nlog", 0, 3);

        Assert.Equal([8], Tinted(editor));
    }

    /// <summary>Byte-for-byte line endings (golden rule 5) are the document's business, not the scan's:
    /// a CRLF file finds the same occurrences a LF one does.</summary>
    [AvaloniaFact]
    public void A_CRLF_document_finds_the_same_occurrences()
    {
        var editor = Over("log\r\nmsg\r\nlog", 0, 3);

        Assert.Equal([10], Tinted(editor));
    }

    /// <summary>Literal and case-sensitive, because that is what ⌘D takes: what lights up is what the
    /// next press would select, and two rules for "the same text" would be one too many.</summary>
    [AvaloniaFact]
    public void Matching_is_case_sensitive()
    {
        Assert.Empty(Tinted(Over("Log log", 0, 3)));
        Assert.Equal([4], Tinted(Over("log log", 0, 3)));
    }

    /// <summary>A chunk is a chunk, not a word: selecting <c>foo</c> finds the <c>foo</c> inside
    /// <c>foobar</c>, which is what makes the highlight useful for a prefix or an id fragment.</summary>
    [AvaloniaFact]
    public void A_selection_matches_inside_words_too()
    {
        var editor = Over("foo foobar", 0, 3);

        Assert.Equal([4], Tinted(editor));
    }

    /// <summary>Non-overlapping, as Find counts matches: in <c>aaaa</c> the two <c>aa</c> are at 0 and 2,
    /// not at 0, 1 and 2.</summary>
    [AvaloniaFact]
    public void Occurrences_do_not_overlap_one_another()
    {
        var editor = Over("aaaa", 0, 2);

        Assert.Equal([2], Tinted(editor));
    }

    /// <summary>
    /// A selection spanning a line break is a structural gesture, not a chunk being eyed — ⌘A would
    /// otherwise light the document up — and keeping to one line is what lets the scan run inside the
    /// text of a row the painter already has.
    /// </summary>
    [AvaloniaFact]
    public void A_selection_that_spans_a_line_break_tints_nothing()
    {
        Assert.Empty(Tinted(Over("one\none\none", 0, 7)));
        Assert.Empty(Tinted(Over("one\r\none", 0, 5)));
    }

    /// <summary>Whitespace would tint nearly every row of an indented file, which answers nothing.</summary>
    [AvaloniaFact]
    public void A_whitespace_only_selection_tints_nothing()
    {
        Assert.Empty(Tinted(Over("a b c", 1, 2)));   // one space
        Assert.Empty(Tinted(Over("a\t\tb\tc", 1, 3))); // two tabs
    }

    /// <summary>A caret is not a search term — the same rule ⌘D goes by.</summary>
    [AvaloniaFact]
    public void A_caret_with_nothing_selected_tints_nothing()
    {
        var editor = new PlainTextEditor { Text = "log log" };
        editor.Buffer.Selection = Selection.At(2);

        Assert.Empty(Tinted(editor));
    }

    /// <summary>There is no minimum length: one character is a legitimate thing to ask about.</summary>
    [AvaloniaFact]
    public void A_single_character_selection_still_tints()
    {
        var editor = Over("a-b-c", 1, 2);

        Assert.Equal([3], Tinted(editor));
    }

    /// <summary>
    /// The tinted ones are exactly what ⌘D has not taken yet, so the highlight is a preview of the
    /// gesture: each press turns the next faint one into a real selection, and when the last one is
    /// taken there is nothing left tinted.
    /// </summary>
    [AvaloniaFact]
    public void What_is_tinted_is_what_Cmd_D_would_take_next()
    {
        var editor = Over("log log log", 0, 3);
        Assert.Equal([4, 8], Tinted(editor));

        editor.AddNextOccurrence();
        Assert.Equal([8], Tinted(editor));

        editor.AddNextOccurrence();
        Assert.Empty(Tinted(editor));
        Assert.True(editor.HasMultipleSelections); // all three are selections now, none of them an "other"
    }

    [AvaloniaFact]
    public void The_highlight_is_on_by_default_and_the_setting_turns_it_off()
    {
        var editor = Over("log log", 0, 3);
        Assert.True(editor.HighlightSelectionOccurrences);
        Assert.Equal([4], Tinted(editor));

        editor.HighlightSelectionOccurrences = false;

        Assert.Empty(Tinted(editor));
    }

    /// <summary>
    /// The same colour as the selection, at less of it. That relationship is the whole design — these are
    /// the same text as what you selected, and the only thing being said is that you didn't select them —
    /// so a second hue would make them a second kind of thing to learn.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_tint_is_the_selection_colour_at_a_lower_alpha(bool dark)
    {
        var (selection, occurrence) = PlainTextEditor.SelectionTintsForTest(dark ? ThemeVariant.Dark : ThemeVariant.Light);

        Assert.Equal((selection.R, selection.G, selection.B), (occurrence.R, occurrence.G, occurrence.B));
        Assert.True(occurrence.A < selection.A, $"the occurrence tint ({occurrence.A:X2}) must be quieter than the selection ({selection.A:X2})");

        // And still louder than the current-row wash under it, or it would be the fainter of two tints
        // drawn on the same row and read as noise in the current line rather than as an answer.
        Assert.True(occurrence.A > PlainTextEditor.CurrentRowTintForTest(dark ? ThemeVariant.Dark : ThemeVariant.Light).A);
    }
}
