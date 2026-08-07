using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// A cell holds more than one line: ⇧↩ breaks the line where the caret is, a Wrap Text cell wraps
/// while you edit it rather than only once committed, and either way the row grows to show what you
/// typed. ↩ still commits — it is decided ahead of the TextBox, which now accepts returns.
/// </summary>
public class InlineEditorMultilineTests
{
    private static void Layout(SpreadsheetGrid grid)
    {
        grid.InvalidateMeasure();
        grid.InvalidateArrange();
        grid.Measure(new Size(640, 400));
        grid.Arrange(new Rect(0, 0, 640, 400));
    }

    private static MainWindow Open()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        Layout(window.Editor.Grid);
        return window;
    }

    [AvaloniaFact]
    public void Shift_return_breaks_the_line_and_plain_return_still_commits()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var grid = window.Editor.Grid;

        env.Selection.MoveTo(0, 0);
        grid.BeginEdit("one");
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Assert.True(grid.IsInlineEditing); // ⇧↩ does not commit...
        Assert.Equal("one\n", grid.EditorText); // ...it breaks the line
        Assert.Equal(4, grid.EditorCaretIndex); // and leaves the caret on the new line

        grid.SetInlineEditorText("one\ntwo");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.False(grid.IsInlineEditing);
        Assert.Equal("one\ntwo", env.Session.ActiveSheet.GetValue(0, 0).AsText);
    }

    [AvaloniaFact]
    public void A_hand_broken_value_grows_its_row_without_being_told_to_wrap()
    {
        var window = Open();
        var sheet = window.Editor.Session.ActiveSheet;
        var grid = window.Editor.Grid;

        sheet.SetValue(0, 0, CellValue.Text("one line"));
        double single = grid.MeasuredRowHeightForTest(0);

        sheet.SetValue(0, 0, CellValue.Text("one line\nand a second"));
        double broken = grid.MeasuredRowHeightForTest(0);

        Assert.True(broken > single, $"a two-line value should grow its row ({single} → {broken})");
    }

    [AvaloniaFact]
    public void A_wrapping_cell_wraps_in_the_editor_and_grows_downward_not_rightward()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        var grid = window.Editor.Grid;

        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, env.Session.ActiveSheet.Name,
            CellRange.Parse("A1"), s => s with { Alignment = s.Alignment with { WrapText = true } }));

        env.Selection.MoveTo(0, 0);
        grid.BeginEdit("hi");
        Layout(grid);
        Assert.True(grid.EditorWraps);
        var shortBounds = grid.EditorBoundsForTest;

        grid.SetInlineEditorText("a much longer value than one column of this grid can hold on a single line");
        Layout(grid);
        var wrapped = grid.EditorBoundsForTest;

        // The column's width *is* the line length when a cell wraps, so widening the editor would
        // contradict the setting — and would put the caret a line above where the text will land.
        Assert.Equal(shortBounds.Width, wrapped.Width, precision: 1);
        Assert.True(wrapped.Height > shortBounds.Height, $"expected the editor to grow downward ({shortBounds.Height} → {wrapped.Height})");
    }

    [AvaloniaFact]
    public void A_cell_that_does_not_wrap_still_grows_rightward()
    {
        // The wrap branch must not swallow the ordinary case: a plain cell keeps the Google-Sheets feel.
        var window = Open();
        IAppEnvironment env = window.Editor;
        var grid = window.Editor.Grid;

        env.Selection.MoveTo(0, 0);
        grid.BeginEdit("hi");
        Layout(grid);
        Assert.False(grid.EditorWraps);
        double narrow = grid.EditorBoundsForTest.Width;

        grid.SetInlineEditorText("a much longer value than one column of this grid can hold");
        Layout(grid);

        Assert.True(grid.EditorBoundsForTest.Width > narrow);
    }
}
