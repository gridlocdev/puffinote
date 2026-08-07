using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Enter on an empty list item leaves the list — the whole block, not just its marker.
///
/// <para>Dropping the marker alone left the caret on an empty line that was still inside the list, and
/// that line is invisible: a blank directly under content is a collapsed paragraph separator, so it draws
/// no row. Enter looked like it had done nothing. Markdown agreed with the renderer — the next line was a
/// lazy continuation of the item — so the paragraph typed there rejoined the bullet it had just left.</para>
///
/// <para>Both faults pushed the user the same way: press Enter again, and again, because nothing appears
/// to happen. The file quietly collected the blank lines those presses made. One newline at the exit fixes
/// the cause, so the extra presses never happen.</para>
/// </summary>
public class ListExitTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    /// <summary>Type an item, Enter for a second one, Enter again to leave the list.</summary>
    private static MarkdownEditor ExitFrom(string text, int caret)
    {
        var e = At(text, caret);
        e.EnterForTest();
        e.EnterForTest();
        return e;
    }

    [AvaloniaFact]
    public void Leaving_a_list_lands_on_a_row_you_can_see()
    {
        var e = ExitFrom("- a", 3);

        Assert.Equal("- a\n\n", e.Text);
        Assert.Equal(2, e.TotalRowsForTest()); // the bullet, and one visible empty line under it
        Assert.Equal(1, e.CaretDrawnRowForTest()); // with the caret drawn on that new row, not snapped back
    }

    [AvaloniaFact]
    public void What_you_type_next_is_a_paragraph_not_more_of_the_bullet()
    {
        // The regression this exists for: "- a\nX" is one list item reading "a X", so the sentence you
        // started after leaving the list was swallowed by it.
        var e = ExitFrom("- a", 3);
        e.TypeForTest('X');

        Assert.Equal("- a\n\nX", e.Text);
        Assert.NotEqual(e.BlockOfLineForTest(0), e.BlockOfLineForTest(2));
    }

    [AvaloniaFact]
    public void Leaving_a_list_mid_document_separates_it_from_what_follows()
    {
        var e = ExitFrom("- a\nnext", 3);

        Assert.Equal("- a\n\n\nnext", e.Text);
        Assert.Equal(3, e.TotalRowsForTest()); // bullet | one visible empty line | next
        Assert.Equal(1, e.CaretDrawnRowForTest());
    }

    [AvaloniaTheory]
    [InlineData("- [ ] a", "- [ ] a\n\n")] // a task item
    [InlineData("1. a", "1. a\n\n")]       // an ordered item
    [InlineData("> a", "> a\n\n")]         // a blockquote
    public void Every_top_level_block_exits_the_same_way(string start, string expected) =>
        Assert.Equal(expected, ExitFrom(start, start.Length).Text);

    [AvaloniaFact]
    public void One_press_is_one_line_once_you_are_out()
    {
        // The count that used to drift: three presses used to buy one visible line, so the habit was to
        // keep pressing. Now the second press exits and the third adds exactly one line.
        var e = ExitFrom("- a", 3);
        e.EnterForTest();

        Assert.Equal("- a\n\n\n", e.Text);
        Assert.Equal(3, e.TotalRowsForTest());
    }

    [AvaloniaFact]
    public void A_nested_item_is_left_where_it_was()
    {
        // Exiting an inner item drops to its indentation — a visible row, still inside the outer list —
        // so there is no invisible line to rescue and nothing to separate it from.
        var e = ExitFrom("- a\n  - b", 9);

        Assert.StartsWith("- a\n  - b\n", e.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", e.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void An_item_inside_a_quote_stays_inside_the_quote()
    {
        // The marker goes; the "> " that says which block you are in does not.
        var e = ExitFrom("> - a", 5);

        Assert.Equal("> - a\n> ", e.Text);
    }

    [AvaloniaFact]
    public void The_exit_writes_the_document_s_own_line_ending()
    {
        var e = ExitFrom("- a\r\nb", 3);

        Assert.Equal("- a\r\n\r\n\r\nb", e.Text);
        Assert.DoesNotContain('\n', e.Text.Replace("\r\n", "", StringComparison.Ordinal));
    }
}
