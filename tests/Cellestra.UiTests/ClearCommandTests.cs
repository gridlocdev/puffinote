using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class ClearCommandTests
{
    [AvaloniaFact]
    public void Clear_formatting_command_resets_style_over_the_selection()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("keep"));
        env.Selection.MoveTo(0, 0);
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true))));

        AppCommands.ById("edit.clearFormats")!.Execute(env);

        Assert.Equal(StylePool.DefaultId, sheet.Find(0, 0)!.StyleId);
        Assert.Equal("keep", sheet.GetValue(0, 0).AsText);
    }

    [AvaloniaFact]
    public void Clear_all_command_empties_the_selection()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("gone"));
        env.Selection.MoveTo(0, 0);

        AppCommands.ById("edit.clearAll")!.Execute(env);

        Assert.Null(sheet.Find(0, 0));
    }
}
