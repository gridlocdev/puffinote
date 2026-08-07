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
        // The claim the whole geometry rests on: a line's row count is ceil(columns / columnsPerRow),
        // so a document's height is known without measuring — or even reading — a single row.
        var (editor, _) = Open(string.Join('\n', Enumerable.Range(0, 20).Select(i => new string('x', i * 40))), wrap: true);

        int expected = 0;
        for (int line = 0; line < 20; line++)
        {
            expected += Math.Max(1, ((line * 40) + editor.ColumnsPerRow - 1) / editor.ColumnsPerRow);
        }

        Assert.Equal(expected, editor.TotalRows);
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
