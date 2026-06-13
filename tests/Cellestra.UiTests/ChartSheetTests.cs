using System.Globalization;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class ChartSheetTests
{
    private static MainWindow WindowWithData()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        string sheet = env.Session.ActiveSheet.Name;

        // A small tabular block: header row + two label rows × two value columns.
        env.Session.Execute(EditCommands.SetInput(sheet, 0, 0, "Region", CultureInfo.InvariantCulture));
        env.Session.Execute(EditCommands.SetInput(sheet, 0, 1, "Q1", CultureInfo.InvariantCulture));
        env.Session.Execute(EditCommands.SetInput(sheet, 1, 0, "North", CultureInfo.InvariantCulture));
        env.Session.Execute(EditCommands.SetInput(sheet, 1, 1, "10", CultureInfo.InvariantCulture));
        env.Session.Execute(EditCommands.SetInput(sheet, 2, 0, "South", CultureInfo.InvariantCulture));
        env.Session.Execute(EditCommands.SetInput(sheet, 2, 1, "20", CultureInfo.InvariantCulture));
        return window;
    }

    [AvaloniaFact]
    public void Adding_a_chart_sheet_shows_the_chart_view_and_hides_the_grid()
    {
        var window = WindowWithData();
        IAppEnvironment env = window;

        env.AddChartSheet();

        Assert.True(env.Session.ActiveSheet.IsChartSheet);
        Assert.True(window.ChartViewVisibleForTest);
        Assert.False(window.FormulaBarVisibleForTest);
        Assert.NotNull(window.ActiveChartViewForTest);
        Assert.Equal("Chart1", env.Session.ActiveSheet.Name);
        Assert.True(env.Session.IsModified);
    }

    [AvaloniaFact]
    public void A_new_chart_sheet_seeds_categories_and_series_from_the_selection()
    {
        var window = WindowWithData();
        IAppEnvironment env = window;
        env.Selection.MoveTo(0, 0);
        env.Selection.ExtendTo(2, 1); // A1:B3

        env.AddChartSheet();

        var chart = env.Session.ActiveSheet.Chart!;
        Assert.Contains("A2:", chart.CategoriesReference); // first column became the categories
        Assert.Single(chart.Series);                       // the one value column became a series
        Assert.Contains("B2:", chart.Series[0].ValuesReference);
        Assert.NotNull(chart.Series[0].NameReference);      // text header row → series name cell
    }

    [AvaloniaFact]
    public void The_chart_view_renders_without_throwing()
    {
        var window = WindowWithData();
        IAppEnvironment env = window;
        env.Selection.MoveTo(0, 0);
        env.Selection.ExtendTo(2, 1);
        env.AddChartSheet();

        // Forcing a real Skia frame exercises ChartControl.Render end to end.
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var resolved = window.ActiveChartViewForTest!.ResolvedForTest;
        Assert.NotNull(resolved);
        Assert.Equal(new[] { 10d, 20d }, resolved!.Series[0].Values);
    }

    [AvaloniaFact]
    public void Editing_the_source_data_updates_the_chart_live()
    {
        var window = WindowWithData();
        IAppEnvironment env = window;
        env.Selection.MoveTo(0, 0);
        env.Selection.ExtendTo(2, 1);
        env.AddChartSheet();
        var view = window.ActiveChartViewForTest!;

        // Change a source value on the data sheet; the session's Changed event drives a live refresh.
        env.Session.Execute(EditCommands.SetInput("Sheet1", 1, 1, "99", CultureInfo.InvariantCulture));

        Assert.Equal(99d, view.ResolvedForTest!.Series[0].Values[0]);
    }

    [AvaloniaFact]
    public void Switching_back_to_a_data_sheet_restores_the_grid()
    {
        var window = WindowWithData();
        IAppEnvironment env = window;
        var dataSheet = env.Session.ActiveSheet;
        env.AddChartSheet();
        Assert.True(window.ChartViewVisibleForTest);

        env.Session.SetActiveSheet(dataSheet);

        Assert.False(window.ChartViewVisibleForTest);
        Assert.True(window.FormulaBarVisibleForTest);
        Assert.Null(window.ActiveChartViewForTest);
    }

    [AvaloniaFact]
    public void Chart_sheet_appears_as_a_tab_and_can_be_listed_with_the_grid_sheets()
    {
        var window = WindowWithData();
        IAppEnvironment env = window;
        env.AddChartSheet();

        var names = env.Session.Workbook.Worksheets.Select(s => s.Name).ToList();
        Assert.Equal(new[] { "Sheet1", "Chart1" }, names);
    }
}
