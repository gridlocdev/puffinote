using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class DataCommandTests
{
    [AvaloniaFact]
    public void Quick_sort_command_sorts_the_current_region()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Score"));
        sheet.SetValue(1, 0, CellValue.Text("Cy")); sheet.SetValue(1, 1, CellValue.Number(3));
        sheet.SetValue(2, 0, CellValue.Text("Ada")); sheet.SetValue(2, 1, CellValue.Number(1));
        sheet.SetValue(3, 0, CellValue.Text("Bev")); sheet.SetValue(3, 1, CellValue.Number(2));
        env.Selection.MoveTo(1, 0); // inside the region, active column = Name

        env.Commands.ById("data.sortAsc")!.Execute();

        Assert.Equal("Name", sheet.GetValue(0, 0).AsText); // header stays
        Assert.Equal("Ada", sheet.GetValue(1, 0).AsText);
        Assert.Equal("Bev", sheet.GetValue(2, 0).AsText);
        Assert.Equal("Cy", sheet.GetValue(3, 0).AsText);
    }

    [AvaloniaFact]
    public void Create_table_command_adds_a_table()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("H"));
        sheet.SetValue(1, 0, CellValue.Number(1));
        env.Selection.MoveTo(0, 0);

        env.Commands.ById("data.createTable")!.Execute();

        Assert.NotNull(sheet.FindTable(0, 0));
        Assert.Equal("Table1", sheet.FindTable(0, 0)!.Name);
    }

    [AvaloniaFact]
    public void Create_table_over_existing_table_does_not_crash_or_add_a_second()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("H"));
        sheet.SetValue(1, 0, CellValue.Number(1));
        env.Selection.MoveTo(0, 0);
        env.Commands.ById("data.createTable")!.Execute();
        Assert.Single(sheet.Tables);

        // Second invocation over the same cells must surface a message, not throw, and add nothing.
        env.Selection.MoveTo(1, 0);
        env.Commands.ById("data.createTable")!.Execute();
        Assert.Single(sheet.Tables);
        Assert.Equal("Table1", sheet.Tables[0].Name);

        // The overlap surfaced the "already in a table" message as a modal; dismiss it so the modal (and
        // its background blur) doesn't keep the headless test process alive after the test returns.
        Assert.True(env.IsModalOpen);
        window.Editor.OverlayHost.Close();
    }

    [AvaloniaFact]
    public void Toggle_filter_sets_and_clears_active_filter()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("H"));
        sheet.SetValue(1, 0, CellValue.Number(1));
        env.Selection.MoveTo(0, 0);

        env.ToggleFilter();
        Assert.NotNull(sheet.ActiveFilter);

        env.ToggleFilter();
        Assert.Null(sheet.ActiveFilter);
    }

    [AvaloniaFact]
    public void Clear_filter_command_clears_lingering_filter_and_sort_state()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("H"));
        sheet.SetValue(1, 0, CellValue.Number(1));
        env.Selection.MoveTo(0, 0);

        // Simulate an applied filter + sort persisted on the database range.
        sheet.ActiveFilterSpec = new Cellestra.Core.Data.FilterSpec(
            new System.Collections.Generic.Dictionary<int, Cellestra.Core.Data.ColumnFilter>
            {
                [0] = new Cellestra.Core.Data.ColumnFilter
                {
                    AllowedValues = new System.Collections.Generic.HashSet<string> { "1" },
                },
            });
        sheet.ActiveSort = new Cellestra.Core.Data.SortSpec(
            new[] { new Cellestra.Core.Data.SortLevel(0, true) });

        env.Commands.ById("data.clearFilter")!.Execute();

        // Clearing must drop the persisted spec/sort so they don't resurrect on reopen.
        Assert.Null(sheet.ActiveFilterSpec);
        Assert.Null(sheet.ActiveSort);
    }
}
