using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

public class FindDialogTests
{
    [AvaloniaFact]
    public void Find_lists_every_match_and_navigates_when_one_is_clicked()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        var sheet = env.Session.ActiveSheet;
        var locale = env.Session.Workbook.Settings.EffectiveLocale;

        env.Session.Execute(EditCommands.SetInput(sheet.Name, 0, 0, "apple", locale));
        env.Session.Execute(EditCommands.SetInput(sheet.Name, 2, 1, "pineapple", locale));

        var dialog = new FindReplaceDialog(window, showReplace: false);

        Assert.Equal(2, dialog.SearchForTest("apple")); // "apple" and "pineapple"

        dialog.SelectMatchForTest(1); // the second match, at C-row... B3 = (row 2, col 1)
        Assert.Equal(2, env.Selection.ActiveRow);
        Assert.Equal(1, env.Selection.ActiveColumn);

        Assert.Equal(0, dialog.SearchForTest("zzz")); // no matches
    }
}
