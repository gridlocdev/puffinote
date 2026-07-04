using Avalonia;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Grid render fixes: Wrap Text grows the row to fit the wrapped lines (task 6), a merged region
/// paints its anchor's fill (task 7, smoke-tested via the real render path), and an error cell's
/// plain-English reason is resolvable for the hover tooltip (task 12).
/// </summary>
public class GridWrapMergeErrorTests
{
    private static (MainWindow Window, Worksheet Sheet, SpreadsheetGrid Grid) Open()
    {
        var window = new MainWindow();
        window.Show();
        return (window, window.Editor.Session.ActiveSheet, window.Editor.Grid);
    }

    private static int WrappedStyleId(MainWindow window)
    {
        var style = window.Editor.Session.Workbook.Styles.Get(StylePool.DefaultId)
            .WithAlignment(new Alignment(WrapText: true));
        return window.Editor.Session.Workbook.Styles.Intern(style);
    }

    [AvaloniaFact]
    public void Wrapping_a_long_string_grows_the_row()
    {
        var (window, sheet, grid) = Open();

        // A long single-line string in a narrow default column.
        const string longText = "The quick brown fox jumps over the lazy dog several times in a row";
        sheet.SetValue(0, 0, CellValue.Text(longText));

        double unwrapped = grid.MeasuredRowHeightForTest(0);

        // Turn on Wrap Text for A1.
        var cell = sheet.GetOrCreate(0, 0);
        cell.StyleId = WrappedStyleId(window);

        double wrapped = grid.MeasuredRowHeightForTest(0);

        Assert.True(wrapped > unwrapped,
            $"wrapped row height ({wrapped}) should exceed the single-line height ({unwrapped})");
    }

    [AvaloniaFact]
    public void Wrapped_render_does_not_throw()
    {
        var (window, sheet, grid) = Open();
        sheet.SetValue(0, 0, CellValue.Text("A long wrapped string that flows onto multiple lines within its column"));
        sheet.GetOrCreate(0, 0).StyleId = WrappedStyleId(window);

        grid.Measure(new Size(800, 600));
        grid.Arrange(new Rect(0, 0, 800, 600));
        window.AssertRenders();
    }

    [AvaloniaFact]
    public void Merged_cell_with_a_fill_renders_without_throwing()
    {
        var (window, sheet, grid) = Open();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3")));
        sheet.SetValue(1, 1, CellValue.Text("merged"));

        // Give the anchor a red fill — DrawMerges must paint it across the region (task 7).
        var styled = window.Editor.Session.Workbook.Styles.Get(StylePool.DefaultId)
            .WithFill(new Fill(ArgbColor.FromRgb(0xE0, 0x10, 0x10)));
        sheet.GetOrCreate(1, 1).StyleId = window.Editor.Session.Workbook.Styles.Intern(styled);

        grid.Measure(new Size(800, 600));
        grid.Arrange(new Rect(0, 0, 800, 600));
        window.AssertRenders();
    }

    [AvaloniaFact]
    public void Error_cell_reason_is_resolvable_for_the_hover_tip()
    {
        var (_, sheet, grid) = Open();
        var error = CellError.Circular("Circular reference involving Sheet1.A1.");
        sheet.SetValue(0, 0, CellValue.FromError(error));

        Assert.Equal("Circular reference involving Sheet1.A1.", grid.ErrorReasonAt(0, 0));
        Assert.Null(grid.ErrorReasonAt(1, 1)); // a non-error cell has no reason
    }

    [AvaloniaFact]
    public void Error_inside_a_merge_reports_via_the_anchor()
    {
        var (_, sheet, grid) = Open();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3")));
        sheet.SetValue(1, 1, CellValue.FromError(CellError.Value("Wrong type.")));

        // Hovering a covered (non-anchor) cell of the merge still surfaces the anchor's reason.
        Assert.Equal("Wrong type.", grid.ErrorReasonAt(2, 2));
    }
}
