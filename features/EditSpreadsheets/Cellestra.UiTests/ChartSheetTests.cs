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
        IAppEnvironment env = window.Editor;
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
        IAppEnvironment env = window.Editor;

        env.AddChartSheet();

        Assert.True(env.Session.ActiveSheet.IsChartSheet);
        Assert.True(window.Editor.ChartViewVisibleForTest);
        Assert.False(window.Editor.FormulaBarVisibleForTest);
        Assert.NotNull(window.Editor.ActiveChartViewForTest);
        Assert.Equal("Chart1", env.Session.ActiveSheet.Name);
        Assert.True(env.Session.IsModified);
    }

    [AvaloniaFact]
    public void A_new_chart_sheet_seeds_categories_and_series_from_the_selection()
    {
        var window = WindowWithData();
        IAppEnvironment env = window.Editor;
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
    public void A_new_chart_autofills_from_the_only_table_on_the_sheet()
    {
        var window = WindowWithData();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        sheet.AddTable(new Cellestra.Core.Data.Table("T", Cellestra.Core.Model.CellRange.Parse("A1:B3"), HasHeader: true));
        env.Selection.MoveTo(8, 8); // selection sits away from the table — the table still drives the chart

        env.AddChartSheet();

        var chart = env.Session.ActiveSheet.Chart!;
        Assert.Contains("A2:A3", chart.CategoriesReference);     // table's first column → labels
        Assert.Single(chart.Series);                              // first numeric column → one series
        Assert.Contains("B2:B3", chart.Series[0].ValuesReference);
        Assert.NotNull(chart.Series[0].NameReference);            // header row → series name cell
    }

    [AvaloniaFact]
    public void The_chart_view_renders_without_throwing()
    {
        var window = WindowWithData();
        IAppEnvironment env = window.Editor;
        env.Selection.MoveTo(0, 0);
        env.Selection.ExtendTo(2, 1);
        env.AddChartSheet();

        // Forcing a real Skia frame exercises ChartControl.Render end to end.
        window.AssertRenders();

        var resolved = window.Editor.ActiveChartViewForTest!.ResolvedForTest;
        Assert.NotNull(resolved);
        Assert.Equal(new[] { 10d, 20d }, resolved!.Series[0].Values);
    }

    [AvaloniaFact]
    public void Editing_the_source_data_updates_the_chart_live()
    {
        var window = WindowWithData();
        IAppEnvironment env = window.Editor;
        env.Selection.MoveTo(0, 0);
        env.Selection.ExtendTo(2, 1);
        env.AddChartSheet();
        var view = window.Editor.ActiveChartViewForTest!;

        // Change a source value on the data sheet; the session's Changed event drives a live refresh.
        env.Session.Execute(EditCommands.SetInput("Sheet1", 1, 1, "99", CultureInfo.InvariantCulture));

        Assert.Equal(99d, view.ResolvedForTest!.Series[0].Values[0]);
    }

    [AvaloniaFact]
    public void Switching_back_to_a_data_sheet_restores_the_grid()
    {
        var window = WindowWithData();
        IAppEnvironment env = window.Editor;
        var dataSheet = env.Session.ActiveSheet;
        env.AddChartSheet();
        Assert.True(window.Editor.ChartViewVisibleForTest);

        env.Session.SetActiveSheet(dataSheet);

        Assert.False(window.Editor.ChartViewVisibleForTest);
        Assert.True(window.Editor.FormulaBarVisibleForTest);
        Assert.Null(window.Editor.ActiveChartViewForTest);
    }

    [AvaloniaFact]
    public void The_sidebar_shows_only_the_fields_each_chart_type_needs()
    {
        var window = WindowWithData();
        IAppEnvironment env = window.Editor;
        env.Selection.MoveTo(0, 0);
        env.Selection.ExtendTo(2, 1);
        env.AddChartSheet();
        var view = window.Editor.ActiveChartViewForTest!;

        // Column (cartesian): categories, axis titles, stacking, and the per-series combo override.
        view.SetTypeForTest(Cellestra.Charts.ChartType.Column);
        var column = view.VisibleFieldsForTest();
        Assert.Contains("Category labels", column);
        Assert.Contains("Stacking", column);
        Assert.Contains("X-axis title", column);
        Assert.Contains("Draw as", column);
        Assert.DoesNotContain("X values", column);
        Assert.DoesNotContain("Bubble size", column);

        // Pie (part-to-whole): slice labels + a single values field, no stacking/axes/combo/X.
        view.SetTypeForTest(Cellestra.Charts.ChartType.Pie);
        var pie = view.VisibleFieldsForTest();
        Assert.Contains("Slice labels", pie);
        Assert.Contains("Slice values", pie);
        Assert.DoesNotContain("Stacking", pie);
        Assert.DoesNotContain("X-axis title", pie);
        Assert.DoesNotContain("Draw as", pie);
        Assert.DoesNotContain("Category labels", pie);

        // Scatter (X/Y): X and Y values, axis titles, but no categories or stacking.
        view.SetTypeForTest(Cellestra.Charts.ChartType.Scatter);
        var scatter = view.VisibleFieldsForTest();
        Assert.Contains("X values", scatter);
        Assert.Contains("Y values", scatter);
        Assert.Contains("X-axis title", scatter);
        Assert.DoesNotContain("Category labels", scatter);
        Assert.DoesNotContain("Stacking", scatter);

        // Bubble adds the third value; Scatter does not.
        Assert.DoesNotContain("Bubble size", scatter);
        view.SetTypeForTest(Cellestra.Charts.ChartType.Bubble);
        Assert.Contains("Bubble size", view.VisibleFieldsForTest());

        // Radar (spokes): spoke labels, no axis titles, no stacking, no combo override.
        view.SetTypeForTest(Cellestra.Charts.ChartType.Radar);
        var radar = view.VisibleFieldsForTest();
        Assert.Contains("Spoke labels", radar);
        Assert.DoesNotContain("X-axis title", radar);
        Assert.DoesNotContain("Stacking", radar);
        Assert.DoesNotContain("Draw as", radar);
    }

    [AvaloniaFact]
    public void Chart_sheet_appears_as_a_tab_and_can_be_listed_with_the_grid_sheets()
    {
        var window = WindowWithData();
        IAppEnvironment env = window.Editor;
        env.AddChartSheet();

        var names = env.Session.Workbook.Worksheets.Select(s => s.Name).ToList();
        Assert.Equal(new[] { "Sheet1", "Chart1" }, names);
    }
}
