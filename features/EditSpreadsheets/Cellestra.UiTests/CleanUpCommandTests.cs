using Avalonia.Headless.XUnit;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The Clean Up commands as the app runs them (PRD §8.17): one undoable step, a real typeface behind
/// the column fitting, and both scopes reachable from the catalog every surface reads.
/// </summary>
public class CleanUpCommandTests
{
    private static (MainWindow Window, IAppEnvironment Env) Sales()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        sheet.SetValue(0, 0, CellValue.Text("Region"));
        sheet.SetValue(0, 1, CellValue.Text("Revenue"));
        sheet.SetValue(1, 0, CellValue.Text("West"));
        sheet.SetValue(1, 1, CellValue.Text("1200.50"));
        sheet.SetValue(2, 0, CellValue.Text("East"));
        sheet.SetValue(2, 1, CellValue.Text("900"));
        return (window, env);
    }

    [AvaloniaFact]
    public void Clean_up_sheet_styles_the_header_and_reads_the_numbers()
    {
        var (_, env) = Sales();
        var sheet = env.Session.ActiveSheet;

        env.Commands.ById("format.cleanUpSheet")!.Execute();

        var header = env.Session.Workbook.Styles.Get(sheet.EffectiveStyleId(0, 0));
        Assert.True(header.Font.Bold);
        Assert.Equal(HorizontalAlignment.Center, header.Alignment.Horizontal);
        Assert.Equal(1200.5, sheet.GetValue(1, 1).AsNumber);
        Assert.Equal(1, sheet.FrozenRows);
    }

    [AvaloniaFact]
    public void One_undo_puts_the_whole_clean_up_back()
    {
        var (_, env) = Sales();
        var sheet = env.Session.ActiveSheet;

        env.Commands.ById("format.cleanUpSheet")!.Execute();
        env.Commands.ById("edit.undo")!.Execute();

        Assert.Equal("1200.50", sheet.GetValue(1, 1).AsText);
        Assert.False(env.Session.Workbook.Styles.Get(sheet.EffectiveStyleId(0, 0)).Font.Bold);
        Assert.Equal(0, sheet.FrozenRows);
    }

    [AvaloniaFact]
    public void Clean_up_workbook_reaches_every_sheet()
    {
        var (_, env) = Sales();
        var second = env.Session.Workbook.AddSheet("Sheet2");
        second.SetValue(0, 0, CellValue.Text("Name"));
        second.SetValue(0, 1, CellValue.Text("Score"));
        second.SetValue(1, 0, CellValue.Text("Ada"));
        second.SetValue(1, 1, CellValue.Text("9"));

        env.Commands.ById("format.cleanUpWorkbook")!.Execute();

        Assert.Equal(9d, second.GetValue(1, 1).AsNumber);
        Assert.True(env.Session.Workbook.Styles.Get(second.EffectiveStyleId(0, 0)).Font.Bold);
    }

    [AvaloniaFact]
    public void A_second_clean_up_changes_nothing_and_costs_no_undo_step()
    {
        var (_, env) = Sales();
        env.Commands.ById("format.cleanUpSheet")!.Execute();
        int depth = UndoDepth(env);

        env.Commands.ById("format.cleanUpSheet")!.Execute();

        Assert.Equal(depth, UndoDepth(env));
    }

    /// <summary>How many undos it takes to empty the stack — the only way to count it from outside.</summary>
    private static int UndoDepth(IAppEnvironment env)
    {
        var session = env.Session;
        int depth = 0;
        while (session.CanUndo)
        {
            session.Undo();
            depth++;
        }

        for (int i = 0; i < depth; i++)
        {
            session.Redo();
        }

        return depth;
    }
}
