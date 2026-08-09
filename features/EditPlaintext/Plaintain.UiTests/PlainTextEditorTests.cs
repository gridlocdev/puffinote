using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Plaintain.Control.Controls;
using Puffinote.Text.Document;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// The surface's semantics: typing, caret movement, selection, undo, and the two things that make it a
/// <i>plaintext</i> editor rather than Typage — the wrap toggle and the line-number gutter.
/// </summary>
public class PlainTextEditorTests
{
    /// <summary>An editor laid out at a known size, so the row/column arithmetic has a viewport to work in.</summary>
    private static (PlainTextEditor Editor, Window Window) Open(string text, int caret = 0, bool wrap = false)
    {
        var editor = new PlainTextEditor { Text = text, WordWrap = wrap };
        var window = new Window { Width = 400, Height = 300, Content = editor };
        window.Show();
        window.UpdateLayout();
        editor.Focus();
        editor.Buffer.Selection = Selection.At(caret);
        return (editor, window);
    }

    [AvaloniaFact]
    public void Typing_inserts_at_the_caret()
    {
        var (editor, window) = Open("hello world", caret: 5);
        window.KeyTextInput(",");

        Assert.Equal("hello, world", editor.Text);
        Assert.Equal(6, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void Enter_writes_the_document_s_own_line_ending()
    {
        var (editor, window) = Open("alpha\r\nbeta", caret: 11);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal("alpha\r\nbeta\r\n", editor.Text);
        Assert.DoesNotContain('\n', editor.Text.Replace("\r\n", "", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void A_vertical_run_remembers_the_column_it_started_in()
    {
        // Down past a short line and back must land where it started, not at the short line's end —
        // the goal column is what makes a run of ↓↓ feel like one gesture instead of two guesses.
        // "longer line here" | "x" | "another long line"
        var (editor, window) = Open("longer line here\nx\nanother long line", caret: 12);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(18, editor.Buffer.Selection.Caret); // clamped to the end of "x"

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(31, editor.Buffer.Selection.Caret); // column 12 of the third line, not its start
    }

    [AvaloniaFact]
    public void Undo_and_redo_walk_the_edit_back_and_forward()
    {
        var (editor, window) = Open("a", caret: 1);
        window.KeyTextInput("b");
        Assert.Equal("ab", editor.Text);

        editor.Undo();
        Assert.Equal("a", editor.Text);
        editor.Redo();
        Assert.Equal("ab", editor.Text);
    }

    [AvaloniaFact]
    public void With_wrap_off_a_line_is_a_row()
    {
        var (editor, _) = Open(string.Join('\n', Enumerable.Repeat(new string('x', 500), 5)));

        Assert.Equal(5, editor.TotalRows);
        Assert.True(editor.CanHorizontallyScroll); // …and the long lines are reached by scrolling sideways
    }

    [AvaloniaFact]
    public void With_wrap_on_a_long_line_becomes_several_rows()
    {
        var (editor, _) = Open(new string('x', 500), wrap: true);

        Assert.True(editor.TotalRows > 1, $"expected the line to fold, got {editor.TotalRows} row(s)");
        Assert.Equal((500 + editor.ColumnsPerRow - 1) / editor.ColumnsPerRow, editor.TotalRows);
        Assert.False(editor.CanHorizontallyScroll); // wrapping means there is nothing to scroll to
    }

    [AvaloniaFact]
    public void The_wrap_row_count_is_arithmetic_not_measurement()
    {
        // The claim the whole geometry rests on: a line's rows are counted in columns, so a document's
        // height is known without laying out or measuring a single row. With nothing to break at, an
        // unbroken run of text still folds exactly at the edge — ceil(columns / columnsPerRow).
        var (editor, _) = Open(string.Join('\n', Enumerable.Range(0, 20).Select(i => new string('x', i * 40))), wrap: true);

        int expected = 0;
        for (int line = 0; line < 20; line++)
        {
            expected += Math.Max(1, ((line * 40) + editor.ColumnsPerRow - 1) / editor.ColumnsPerRow);
        }

        Assert.Equal(expected, editor.TotalRows);
    }

    /// <summary>
    /// The point of word wrap: a fold lands between words, so no word is cut in half by the window's edge.
    /// </summary>
    [AvaloniaFact]
    public void Wrapping_breaks_between_words_not_inside_them()
    {
        var (editor, _) = Open("", wrap: true);
        int perRow = editor.ColumnsPerRow;

        // A line of five-letter words, long enough that the first row cannot hold a whole number of them:
        // the fold has to give up the columns between the last word that fits and the edge.
        string word = "abcde";
        var words = Enumerable.Repeat(word, (perRow / (word.Length + 1)) + 4).ToList();
        editor.Text = string.Join(' ', words);

        Assert.True(editor.TotalRows > 1);
        var (_, secondRowStart) = SplitAtSecondRow(editor);

        Assert.Equal(0, secondRowStart % (word.Length + 1)); // a word's first letter, never its middle
        Assert.True(secondRowStart <= perRow, $"the second row starts at {secondRowStart}, past the fold");
    }

    /// <summary>A word with nowhere to break still has to fit somewhere — it breaks at the edge.</summary>
    [AvaloniaFact]
    public void A_word_longer_than_a_row_still_breaks_at_the_edge()
    {
        var (editor, _) = Open("", wrap: true);
        int perRow = editor.ColumnsPerRow;
        editor.Text = new string('x', perRow * 2);

        Assert.Equal(2, editor.TotalRows);
        Assert.Equal(perRow, SplitAtSecondRow(editor).SecondRowStart);
    }

    /// <summary>
    /// Blanks are allowed to hang past the right edge rather than pushing the next word down a row: a run
    /// of spaces at a fold draws nothing, and moving it would leave the row before it ending early.
    /// </summary>
    [AvaloniaFact]
    public void Trailing_blanks_hang_past_the_edge_instead_of_forcing_a_fold()
    {
        var (editor, _) = Open("", wrap: true);
        int perRow = editor.ColumnsPerRow;
        editor.Text = new string('x', perRow - 2) + "    y"; // the spaces cross the edge; "y" is past it

        Assert.Equal(2, editor.TotalRows);
        Assert.Equal(perRow + 2, SplitAtSecondRow(editor).SecondRowStart); // the row starts at "y"
    }

    /// <summary>
    /// A few hanging blanks are invisible; a whole row of them is a place the caret could hide, and
    /// wrapping has no sideways scroll to go and find it. So the hang is capped at one row's width.
    /// </summary>
    [AvaloniaFact]
    public void A_blank_run_wider_than_the_window_folds_rather_than_hanging_forever()
    {
        var (editor, _) = Open("", wrap: true);
        int perRow = editor.ColumnsPerRow;
        editor.Text = new string(' ', perRow * 4) + "tail";

        Assert.True(editor.TotalRows >= 4, $"the blank run should fold, got {editor.TotalRows} row(s)");

        // Every column of the line is reachable within a row's width of that row's start — which is the
        // whole claim: nothing sits past an edge you cannot scroll to.
        for (int offset = 0; offset <= editor.Text.Length; offset++)
        {
            editor.MoveCaret(offset, extend: false);
            Assert.InRange(editor.CaretRowColumn.Column, 0, perRow);
        }
    }

    /// <summary>The caret's row/column and the offsets a row spans agree about where the fold is.</summary>
    [AvaloniaFact]
    public void The_caret_lands_on_the_row_the_fold_put_its_word_on()
    {
        var (editor, _) = Open("", wrap: true);
        int perRow = editor.ColumnsPerRow;
        editor.Text = new string('a', perRow - 3) + " bcdef";

        int wordStart = perRow - 2; // the offset of "b" — folded onto the second row
        editor.MoveCaret(wordStart + 2, extend: false);

        var (row, column) = editor.CaretRowColumn;
        Assert.Equal(1, row);
        Assert.Equal(2, column);
        Assert.Equal(wordStart, editor.RowStartOffset());
        Assert.Equal(editor.Text.Length, editor.RowEndOffset());
    }

    /// <summary>The offset the second visual row begins at, found through the row/offset map itself.</summary>
    private static (int Rows, int SecondRowStart) SplitAtSecondRow(PlainTextEditor editor)
    {
        editor.MoveCaret(editor.Text.Length, extend: false); // anywhere on the last row
        while (editor.CaretRowColumn.Row > 1)
        {
            editor.MoveByRow(-1, extend: false);
        }

        editor.MoveCaret(editor.RowStartOffset(), extend: false);
        return (editor.TotalRows, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void A_tab_advances_to_the_next_tab_stop_not_by_one()
    {
        // Column arithmetic has to agree with what is painted, or every column below a tab is a lie —
        // and columns are the whole reason anyone opens a log in a monospace editor.
        var (editor, _) = Open("\tx");

        Assert.Equal(PlainTextEditor.TabWidth, editor.ColumnOf(0, 1));
        Assert.Equal(PlainTextEditor.TabWidth + 1, editor.LineColumns(0));
    }

    [AvaloniaFact]
    public void The_gutter_widens_with_the_largest_line_number()
    {
        var (few, _) = Open("a\nb\nc");
        var (many, _) = Open(string.Join('\n', Enumerable.Repeat("x", 5000)));

        Assert.True(many.GutterWidth > few.GutterWidth);
        Assert.Equal(0, new PlainTextEditor { ShowLineNumbers = false }.GutterWidth);
    }

    [AvaloniaFact]
    public void Home_goes_to_the_content_then_to_the_true_start()
    {
        var (editor, _) = Open("    indented", caret: 12);

        editor.MoveCaret(editor.SmartHomeOffset(), extend: false);
        Assert.Equal(4, editor.Buffer.Selection.Caret); // the first real character

        editor.MoveCaret(editor.SmartHomeOffset(), extend: false);
        Assert.Equal(0, editor.Buffer.Selection.Caret); // pressed again: the line's true start
    }

    [AvaloniaFact]
    public void A_double_click_selects_the_word_it_landed_on_and_nothing_else()
    {
        var (editor, _) = Open("alpha beta gamma");
        editor.SelectWordAt(8); // inside "beta"

        Assert.Equal(6, editor.Buffer.Selection.Start);
        Assert.Equal(10, editor.Buffer.Selection.End);
    }

    [AvaloniaFact]
    public void Tab_indents_to_the_next_stop_rather_than_by_a_fixed_four()
    {
        var (editor, window) = Open("ab", caret: 2);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.Equal("ab  ", editor.Text); // column 2 → column 4, so two spaces, not four
    }

    [AvaloniaFact]
    public void The_extent_covers_the_document_while_the_control_stays_viewport_sized()
    {
        // The virtualization contract: a million rows is a tall *extent*, not a tall control.
        var (editor, _) = Open(string.Join('\n', Enumerable.Repeat("line", 5000)));

        Assert.True(editor.Extent.Height > 5000 * editor.RowHeight * 0.9);
        Assert.True(editor.Bounds.Height <= 300);
    }
}
