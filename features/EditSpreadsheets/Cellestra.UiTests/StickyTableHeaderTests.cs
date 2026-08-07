using Avalonia;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Scrolled into a table, the column-header strip shows the table's own column titles instead of
/// A/B/C — and gives them back the moment you leave it.
/// </summary>
public class StickyTableHeaderTests
{
    private static void Layout(SpreadsheetGrid grid)
    {
        grid.InvalidateMeasure();
        grid.InvalidateArrange();
        grid.Measure(new Size(640, 400));
        grid.Arrange(new Rect(0, 0, 640, 400));
    }

    private static MainWindow TableWindow()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;

        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Team"));
        sheet.SetValue(0, 1, CellValue.Text("Points"));
        for (int row = 1; row <= 40; row++)
        {
            sheet.SetValue(row, 0, CellValue.Text($"Row {row}"));
            sheet.SetValue(row, 1, CellValue.Number(row));
        }

        sheet.AddTable(new Table("T", CellRange.Parse("A1:B41"), HasHeader: true));
        window.Editor.Grid.Refresh();
        Layout(window.Editor.Grid);
        return window;
    }

    private static void ScrollTo(MainWindow window, int row)
    {
        window.Editor.Grid.DragVerticalScrollForTest(row);
        Layout(window.Editor.Grid);
    }

    [AvaloniaFact]
    public void At_the_top_the_strip_still_reads_A_and_B()
    {
        var window = TableWindow();
        Assert.Equal("A", window.Editor.Grid.ColumnHeaderLabelForTest(0));
        Assert.Equal("B", window.Editor.Grid.ColumnHeaderLabelForTest(1));
    }

    [AvaloniaFact]
    public void Scrolled_into_the_table_the_strip_reads_the_table_titles()
    {
        var window = TableWindow();
        ScrollTo(window, 10);

        Assert.Equal("Team", window.Editor.Grid.ColumnHeaderLabelForTest(0));
        Assert.Equal("Points", window.Editor.Grid.ColumnHeaderLabelForTest(1));

        // A column the table doesn't cover keeps its letter — the strip isn't wholly taken over.
        Assert.Equal("D", window.Editor.Grid.ColumnHeaderLabelForTest(3));
    }

    [AvaloniaFact]
    public void Scrolling_past_the_table_gives_the_letters_back()
    {
        var window = TableWindow();
        ScrollTo(window, 10);
        Assert.Equal("Team", window.Editor.Grid.ColumnHeaderLabelForTest(0));

        ScrollTo(window, 50); // past A1:B41 entirely
        Assert.Equal("A", window.Editor.Grid.ColumnHeaderLabelForTest(0));

        ScrollTo(window, 0); // and back to the top, where the real header row is on screen
        Assert.Equal("A", window.Editor.Grid.ColumnHeaderLabelForTest(0));
    }
}
