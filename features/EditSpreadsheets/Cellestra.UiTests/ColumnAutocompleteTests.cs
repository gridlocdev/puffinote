using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Type-ahead from the column above (#20). What it offers, and — more importantly — when it gets out
/// of the way: the capitalisation you type is a choice, not a typo to be corrected.
/// </summary>
public class ColumnAutocompleteTests
{
    private static (MainWindow Window, IAppEnvironment Env) Column(params string[] valuesAbove)
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        for (int row = 0; row < valuesAbove.Length; row++)
        {
            sheet.SetValue(row, 0, CellValue.Text(valuesAbove[row]));
        }

        window.Editor.Selection.MoveTo(valuesAbove.Length, 0);
        return (window, window.Editor);
    }

    private static string TypeInto(MainWindow window, string typed)
    {
        var grid = window.Editor.Grid;
        grid.BeginEdit(string.Empty);
        foreach (char c in typed)
        {
            window.KeyTextInput(c.ToString());
        }

        return grid.EditorText;
    }

    [AvaloniaFact]
    public void A_matching_prefix_is_completed_from_the_entry_above()
    {
        var (window, _) = Column("Avocado");
        Assert.Equal("Avocado", TypeInto(window, "Av"));
    }

    [AvaloniaFact]
    public void Typing_a_different_case_stops_the_completion_and_keeps_what_you_typed()
    {
        // "avocado" under a column of "Avocado" is a deliberate lower-case entry. An insensitive match
        // would complete it *and* rewrite the "a" you typed into an "A" on the way.
        var (window, _) = Column("Avocado");
        Assert.Equal("av", TypeInto(window, "av"));
    }

    [AvaloniaFact]
    public void The_completion_never_rewrites_the_characters_you_already_typed()
    {
        var (window, _) = Column("Avocado");
        string text = TypeInto(window, "Avo");
        Assert.StartsWith("Avo", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_tables_header_is_a_title_not_an_entry_to_complete_from()
    {
        // A1 is the table's header ("Fruit"), A2 its first entry. Typing "F" into A3 must not be
        // completed to the column's own title — only to a value some earlier row put in the column.
        var window = new MainWindow();
        window.Show();
        var env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Fruit"));
        sheet.SetValue(1, 0, CellValue.Text("Apple"));
        env.Session.Execute(DataEditCommands.CreateTable(env.Session.Workbook, sheet.Name,
            new Table("Fruits", CellRange.Parse("A1:A2"))));
        env.Selection.MoveTo(2, 0);

        Assert.Equal("F", TypeInto(window, "F"));
    }

    [AvaloniaFact]
    public void A_tables_own_entries_still_complete()
    {
        // The header is skipped, not the whole table: the values under it are exactly the list.
        var window = new MainWindow();
        window.Show();
        var env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Fruit"));
        sheet.SetValue(1, 0, CellValue.Text("Apple"));
        env.Session.Execute(DataEditCommands.CreateTable(env.Session.Workbook, sheet.Name,
            new Table("Fruits", CellRange.Parse("A1:A2"))));
        env.Selection.MoveTo(2, 0);

        Assert.Equal("Apple", TypeInto(window, "Ap"));
    }

    [AvaloniaFact]
    public void Only_the_contiguous_block_above_the_cell_is_a_list()
    {
        // A1 "Avocado", A2 empty, A3 is where we type: the gap ends the list, so there is nothing to
        // complete from. (This is also what keeps the lookup off the whole sheet on every keystroke.)
        var window = new MainWindow();
        window.Show();
        window.Editor.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("Avocado"));
        window.Editor.Selection.MoveTo(2, 0);

        Assert.Equal("Av", TypeInto(window, "Av"));
    }
}
