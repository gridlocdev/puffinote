using System.Globalization;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class SpreadsheetGridTests
{
    [AvaloniaFact]
    public void Editing_through_the_session_recalculates_and_renders()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Session.Execute(EditCommands.SetInput(sheet.Name, 0, 0, "10", CultureInfo.InvariantCulture));
        env.Session.Execute(EditCommands.SetInput(sheet.Name, 1, 0, "=A1*2", CultureInfo.InvariantCulture));

        Assert.Equal(10, sheet.GetValue(0, 0).AsNumber);
        Assert.Equal(20, sheet.GetValue(1, 0).AsNumber); // formula recalculated, grid rendered on Show()
    }

    [AvaloniaFact]
    public void Selection_navigation_updates_active_cell()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        env.Selection.MoveTo(3, 4);
        Assert.Equal(3, env.Selection.ActiveRow);
        Assert.Equal(4, env.Selection.ActiveColumn);

        env.Selection.ExtendTo(5, 6);
        Assert.Equal(3, env.Selection.Top);
        Assert.Equal(5, env.Selection.Bottom);
        Assert.Equal(4, env.Selection.Left);
        Assert.Equal(6, env.Selection.Right);
    }

    [AvaloniaFact]
    public void Undo_through_session_reverts_edit()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Session.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Number(7)));
        Assert.Equal(7, sheet.GetValue(0, 0).AsNumber);

        env.Session.Undo();
        Assert.True(sheet.GetValue(0, 0).IsEmpty);
    }
}
