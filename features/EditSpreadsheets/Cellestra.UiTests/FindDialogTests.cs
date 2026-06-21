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
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        var locale = env.Session.Workbook.Settings.EffectiveLocale;

        env.Session.Execute(EditCommands.SetInput(sheet.Name, 0, 0, "apple", locale));
        env.Session.Execute(EditCommands.SetInput(sheet.Name, 2, 1, "pineapple", locale));

        var dialog = new FindReplaceDialog(window.Editor, showReplace: false);

        Assert.Equal(2, dialog.SearchForTest("apple")); // "apple" and "pineapple"

        dialog.SelectMatchForTest(1); // the second match, at C-row... B3 = (row 2, col 1)
        Assert.Equal(2, env.Selection.ActiveRow);
        Assert.Equal(1, env.Selection.ActiveColumn);

        Assert.Equal(0, dialog.SearchForTest("zzz")); // no matches
    }

    [AvaloniaFact]
    public void Replace_rewrites_numeric_cells_and_keeps_a_numeric_result_numeric()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        var locale = env.Session.Workbook.Settings.EffectiveLocale;

        // A plain number whose display contains "2"; replacing 2→9 turns 123 into 193 (still a number).
        env.Session.Execute(EditCommands.SetInput(sheet.Name, 0, 0, "123", locale));

        var dialog = new FindReplaceDialog(window.Editor, showReplace: true);
        Assert.Equal(1, dialog.SearchForTest("2"));   // the numeric cell matches on its displayed value
        dialog.ReplaceAllForTest("2", "9");

        var value = sheet.GetValue(0, 0);
        Assert.True(value.IsNumber);                   // a still-numeric result stays a number
        Assert.Equal(193, value.AsNumber, 9);
    }

    [AvaloniaFact]
    public void Replace_matches_and_rewrites_inside_a_formula_source()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        var locale = env.Session.Workbook.Settings.EffectiveLocale;

        env.Session.Execute(EditCommands.SetInput(sheet.Name, 0, 0, "10", locale));   // A1
        env.Session.Execute(EditCommands.SetInput(sheet.Name, 1, 0, "20", locale));   // A2
        env.Session.Execute(EditCommands.SetInput(sheet.Name, 2, 0, "30", locale));   // A3
        env.Session.Execute(EditCommands.SetFormula(sheet.Name, 4, 0, "A1+A2"));      // A5 = =A1+A2 → 30

        var dialog = new FindReplaceDialog(window.Editor, showReplace: true);

        // The formula cell matches on its SOURCE text "=A1+A2" (not on its computed display "30").
        Assert.Equal(1, dialog.SearchForTest("A2"));

        // Rewriting A2→A3 retargets the reference and the formula recomputes (10+30 = 40).
        Assert.True(dialog.ReplaceCellForTest(4, 0, "A2", "A3"));
        var cell = sheet.Find(4, 0);
        Assert.NotNull(cell);
        Assert.True(cell!.IsFormula);
        Assert.Equal("A1+A3", cell.Formula);
        Assert.Equal(40, sheet.GetValue(4, 0).AsNumber, 9);
    }

    [AvaloniaFact]
    public void Find_scopes_a_formula_cell_to_its_source_not_its_display()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        // =1+2 displays "3", but Find scopes a formula cell to its source text, so the digit "3"
        // doesn't match the formula; searching the source ("1+2") does.
        env.Session.Execute(EditCommands.SetFormula(sheet.Name, 0, 0, "1+2"));

        var dialog = new FindReplaceDialog(window.Editor, showReplace: true);
        Assert.Equal(0, dialog.SearchForTest("3"));
        Assert.Equal(1, dialog.SearchForTest("1+2"));
    }
}
