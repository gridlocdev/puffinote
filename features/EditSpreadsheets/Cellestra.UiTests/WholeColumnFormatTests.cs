using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Clicking a column header and hitting Wrap Text — the whole path, through the real command. The
/// format lands on the column, not on its 1,048,576 cells, so it is instant, it leaves the sheet's
/// used range alone, and the toggle still knows it is on when you come back to it.
/// </summary>
public class WholeColumnFormatTests
{
    private static MainWindow Open()
    {
        var window = new MainWindow();
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void Wrapping_a_whole_column_is_instant_and_leaves_the_used_range_alone()
    {
        var window = Open();
        IAppEnvironment env = window.Editor;
        env.Session.ActiveSheet.SetValue(0, 0, Cellestra.Core.Values.CellValue.Text("hello"));
        env.Selection.SetRange(0, 0, SheetDimensions.MaxRows - 1, 0);

        var watch = Stopwatch.StartNew();
        window.Editor.Commands.ById("format.wrap")!.Execute();
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 500, $"whole-column wrap took {watch.ElapsedMilliseconds}ms");
        Assert.Equal(CellRange.Parse("A1"), env.Session.ActiveSheet.GetUsedRange());
        Assert.Single(env.Session.ActiveSheet.EnumerateCells());
    }

    [AvaloniaFact]
    public void The_wrap_toggle_reads_its_state_from_the_column_and_turns_back_off()
    {
        // The toggle asks "is the active cell wrapped?". With the format on the column and the active
        // cell blank, answering from the cell alone would report off — and the second press would turn
        // wrap on again instead of off.
        var window = Open();
        IAppEnvironment env = window.Editor;
        var wrap = window.Editor.Commands.ById("format.wrap")!;

        env.Selection.SetRange(0, 0, SheetDimensions.MaxRows - 1, 0);
        wrap.Execute();
        Assert.True(wrap.IsActive!());

        wrap.Execute();
        Assert.False(wrap.IsActive!());
        Assert.False(env.Session.Workbook.Styles.Get(env.Session.ActiveSheet.EffectiveStyleId(9, 0)).Alignment.WrapText);
    }
}
