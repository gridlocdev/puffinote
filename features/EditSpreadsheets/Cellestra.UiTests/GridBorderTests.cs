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
/// Cell-border rendering: the grid draws each cell's <see cref="CellStyle.Border"/> on top of the
/// gridlines (smoke-tested through the real Skia render path), merged regions border the whole rect,
/// and the pure <see cref="BorderPenStyle"/> mapper gives heavier pens for heavier styles and dashes
/// for dashed/dotted.
/// </summary>
public class GridBorderTests
{
    private static (MainWindow Window, Worksheet Sheet, SpreadsheetGrid Grid) Open()
    {
        var window = new MainWindow();
        window.Show();
        return (window, window.Editor.Session.ActiveSheet, window.Editor.Grid);
    }

    private static int BorderedStyleId(MainWindow window, Border border)
    {
        var style = window.Editor.Session.Workbook.Styles.Get(StylePool.DefaultId).WithBorder(border);
        return window.Editor.Session.Workbook.Styles.Intern(style);
    }

    [AvaloniaFact]
    public void Bordered_cell_renders_without_throwing()
    {
        var (window, sheet, grid) = Open();
        sheet.SetValue(0, 0, CellValue.Number(1));
        // A cell with every edge style exercised across a few cells.
        sheet.GetOrCreate(0, 0).StyleId = BorderedStyleId(window, Border.All(BorderLineStyle.Thin));
        sheet.SetValue(1, 0, CellValue.Number(2));
        sheet.GetOrCreate(1, 0).StyleId = BorderedStyleId(window, new Border(
            BorderEdge.Of(BorderLineStyle.Dashed), BorderEdge.Of(BorderLineStyle.Dotted),
            BorderEdge.Of(BorderLineStyle.Thick), BorderEdge.Of(BorderLineStyle.Double)));

        grid.Measure(new Size(800, 600));
        grid.Arrange(new Rect(0, 0, 800, 600));
        window.AssertRenders();
    }

    [AvaloniaFact]
    public void Merged_bordered_cell_renders_without_throwing()
    {
        var (window, sheet, grid) = Open();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3")));
        sheet.SetValue(1, 1, CellValue.Text("merged"));
        sheet.GetOrCreate(1, 1).StyleId = BorderedStyleId(window, Border.All(BorderLineStyle.Medium));

        grid.Measure(new Size(800, 600));
        grid.Arrange(new Rect(0, 0, 800, 600));
        window.AssertRenders();
    }

    [Theory]
    [InlineData(BorderLineStyle.Thin)]
    [InlineData(BorderLineStyle.Medium)]
    [InlineData(BorderLineStyle.Thick)]
    [InlineData(BorderLineStyle.Double)]
    public void Pen_thickness_is_at_least_one_pixel(BorderLineStyle style)
    {
        var (thin, _) = BorderPenStyle.For(style, zoom: 1.0);
        Assert.True(thin >= 1.0);
    }

    [Fact]
    public void Heavier_styles_get_heavier_pens()
    {
        double thin = BorderPenStyle.For(BorderLineStyle.Thin, 1.0).Thickness;
        double medium = BorderPenStyle.For(BorderLineStyle.Medium, 1.0).Thickness;
        double thick = BorderPenStyle.For(BorderLineStyle.Thick, 1.0).Thickness;
        Assert.True(medium > thin);
        Assert.True(thick > medium);
    }

    [Fact]
    public void Dashed_and_dotted_carry_a_dash_pattern_others_do_not()
    {
        Assert.NotNull(BorderPenStyle.For(BorderLineStyle.Dashed, 1.0).Dash);
        Assert.NotNull(BorderPenStyle.For(BorderLineStyle.Dotted, 1.0).Dash);
        Assert.Null(BorderPenStyle.For(BorderLineStyle.Thin, 1.0).Dash);
        Assert.Null(BorderPenStyle.For(BorderLineStyle.Thick, 1.0).Dash);
    }

    [Fact]
    public void Thick_pen_scales_up_with_zoom()
    {
        double atOne = BorderPenStyle.For(BorderLineStyle.Thick, 1.0).Thickness;
        double atTwo = BorderPenStyle.For(BorderLineStyle.Thick, 2.0).Thickness;
        Assert.True(atTwo > atOne);
    }
}
