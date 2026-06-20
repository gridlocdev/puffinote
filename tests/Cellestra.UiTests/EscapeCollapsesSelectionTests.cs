using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Esc returns the grid to its non-selected state at the cell the selection started from: a first Esc
/// cancels any pending copy/cut marquee (keeping the selection), and once there's no marquee Esc
/// collapses a multi-cell selection back to its origin (anchor) cell.
/// </summary>
public class EscapeCollapsesSelectionTests
{
    private static MainWindow OpenFocused()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Focus();
        return window;
    }

    [AvaloniaFact]
    public void Esc_collapses_a_dragged_selection_back_to_its_origin()
    {
        var window = OpenFocused();
        window.Editor.Grid.SelectForTest(2, 1);                 // anchor at the start cell
        window.Editor.Grid.NavigateForTest(2, 2, extend: true); // extend out to a block
        Assert.False(window.Editor.Selection.IsSingleCell);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.True(window.Editor.Selection.IsSingleCell);
        Assert.Equal(2, window.Editor.Selection.ActiveRow);
        Assert.Equal(1, window.Editor.Selection.ActiveColumn);
    }

    [AvaloniaFact]
    public void First_Esc_clears_the_marquee_and_keeps_the_selection()
    {
        var window = OpenFocused();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("x"));

        window.Editor.Grid.SelectForTest(2, 1);
        window.Editor.Grid.NavigateForTest(2, 2, extend: true);
        window.Editor.Grid.ShowMarquee(CellRange.Parse("A1"), isCut: false, sheetName: sheet.Name);
        Assert.True(window.Editor.Grid.HasMarquee);

        // First Esc: the marquee goes, the selection stays.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(window.Editor.Grid.HasMarquee);
        Assert.False(window.Editor.Selection.IsSingleCell);

        // Second Esc: now the selection collapses to its origin.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(window.Editor.Selection.IsSingleCell);
        Assert.Equal(2, window.Editor.Selection.ActiveRow);
        Assert.Equal(1, window.Editor.Selection.ActiveColumn);
    }
}
