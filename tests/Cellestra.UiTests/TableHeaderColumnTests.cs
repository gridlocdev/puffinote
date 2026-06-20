using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Designating a table's first column as a header column (task 5): the command is offered only
/// inside a table, toggles the table's HasHeaderColumn, and re-renders without throwing.
/// </summary>
public class TableHeaderColumnTests
{
    private static MainWindow TableWindow()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Grid.Zoom = 1.0;
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Team")); sheet.SetValue(0, 1, CellValue.Text("Pts"));
        sheet.SetValue(1, 0, CellValue.Text("A")); sheet.SetValue(1, 1, CellValue.Number(3));
        sheet.AddTable(new Table("T", CellRange.Parse("A1:B2"), HasHeader: true));
        window.Editor.Grid.Refresh();
        window.CaptureRenderedFrame();
        return window;
    }

    [AvaloniaFact]
    public void The_command_is_enabled_only_inside_a_table()
    {
        var window = TableWindow();
        var env = (IAppEnvironment)window.Editor;
        var command = AppCommands.ById("data.headerColumn")!;

        env.Selection.MoveTo(0, 0); // inside the table
        Assert.True(command.IsEnabled(env));

        env.Selection.MoveTo(20, 20); // far outside any table
        Assert.False(command.IsEnabled(env));
    }

    [AvaloniaFact]
    public void Toggling_designates_then_clears_the_header_column()
    {
        var window = TableWindow();
        var env = (IAppEnvironment)window.Editor;
        env.Selection.MoveTo(1, 0); // a data cell inside the table

        Assert.False(env.ActiveTable!.HasHeaderColumn);
        env.ToggleTableHeaderColumn();
        Assert.True(env.ActiveTable!.HasHeaderColumn);
        Assert.NotNull(window.CaptureRenderedFrame()); // the header-column band renders without throwing

        env.ToggleTableHeaderColumn();
        Assert.False(env.ActiveTable!.HasHeaderColumn);
    }
}
