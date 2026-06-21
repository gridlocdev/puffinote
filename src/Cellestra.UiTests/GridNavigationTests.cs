using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>⌘+Arrow edge-jump navigation (#1) and Shift+click header band selection (#3).</summary>
public class GridNavigationTests
{
    [AvaloniaFact]
    public void Cmd_arrow_jumps_to_the_edge_of_the_data_block()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetValue(1, 0, CellValue.Number(2));
        sheet.SetValue(2, 0, CellValue.Number(3));
        window.Editor.Selection.MoveTo(0, 0);

        window.Editor.Grid.JumpEdgeForTest(1, 0, extend: false); // ⌘+Down

        Assert.Equal(2, window.Editor.Selection.ActiveRow); // jumped to the bottom of the run (A3)
        Assert.Equal(0, window.Editor.Selection.ActiveColumn);
    }

    [AvaloniaFact]
    public void Cmd_shift_arrow_extends_the_selection_to_the_edge()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetValue(1, 0, CellValue.Number(2));
        sheet.SetValue(2, 0, CellValue.Number(3));
        window.Editor.Selection.MoveTo(0, 0);

        window.Editor.Grid.JumpEdgeForTest(1, 0, extend: true); // ⌘+Shift+Down

        Assert.Equal(0, window.Editor.Selection.Top);
        Assert.Equal(2, window.Editor.Selection.Bottom); // selection now spans A1:A3
    }

    [Fact]
    public void Shift_click_row_header_selects_the_band_between_anchors()
    {
        var selection = new GridSelection();
        selection.SetRange(2, 0, 2, SheetDimensions.MaxColumns - 1); // click row 3's header

        selection.ExtendRowBand(5); // Shift+click row 6's header

        Assert.Equal(2, selection.Top);
        Assert.Equal(5, selection.Bottom);
        Assert.Equal(0, selection.Left);
        Assert.Equal(SheetDimensions.MaxColumns - 1, selection.Right); // full width
    }

    [Fact]
    public void Shift_click_column_header_selects_the_full_height_band()
    {
        var selection = new GridSelection();
        selection.SetRange(0, 1, SheetDimensions.MaxRows - 1, 1); // click column B's header

        selection.ExtendColumnBand(4); // Shift+click column E's header

        Assert.Equal(1, selection.Left);
        Assert.Equal(4, selection.Right);
        Assert.Equal(0, selection.Top);
        Assert.Equal(SheetDimensions.MaxRows - 1, selection.Bottom); // full height
    }
}
