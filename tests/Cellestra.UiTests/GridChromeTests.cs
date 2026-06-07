using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class GridChromeTests
{
    [AvaloniaFact]
    public void Merged_region_renders_without_throwing()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("merged"));
        env.Selection.SetRange(0, 0, 1, 1);
        AppCommands.ById("format.merge")!.Execute(env);

        Assert.NotNull(sheet.FindMerge(0, 0));
        Assert.Equal(CellRange.Parse("A1:B2"), sheet.FindMerge(0, 0)!.Range);
    }

    [AvaloniaFact]
    public void Freeze_panes_command_sets_and_clears_freeze()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        var sheet = env.Session.ActiveSheet;

        env.Selection.MoveTo(2, 3);
        AppCommands.ById("view.freezePanes")!.Execute(env);
        Assert.Equal(2, sheet.FrozenRows);
        Assert.Equal(3, sheet.FrozenColumns);

        AppCommands.ById("view.freezePanes")!.Execute(env);
        Assert.Equal(0, sheet.FrozenRows);
        Assert.Equal(0, sheet.FrozenColumns);
    }
}
