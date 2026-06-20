using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class UndoSelectionTests
{
    [AvaloniaFact]
    public void Undo_returns_the_selection_to_the_edit_site()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Selection.MoveTo(0, 0);
        env.Session.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Number(7)));
        env.Selection.MoveTo(4, 4); // wander off

        env.Session.Undo();

        Assert.True(sheet.GetValue(0, 0).IsEmpty);
        Assert.Equal(0, env.Selection.ActiveRow);     // selection followed the undo back to A1
        Assert.Equal(0, env.Selection.ActiveColumn);

        env.Session.Redo();
        Assert.Equal(7, sheet.GetValue(0, 0).AsNumber);
    }
}
