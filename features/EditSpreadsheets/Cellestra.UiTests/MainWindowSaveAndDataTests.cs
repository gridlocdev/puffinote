using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Cellestra.Editor;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Covers the MainWindow file-safety and data-tool fixes: the unsaved-changes save-prompt decision
/// (#1/#2 close/New/Open/Import), the Text-to-Columns overwrite scan (#5), and the filter toggle's
/// state cleanup plus multi-level Sort persistence.
/// </summary>
public class MainWindowSaveAndDataTests
{
    // ---- #1/#2: save-changes prompt decision ----

    [Fact]
    public void Unmodified_document_proceeds_without_prompting()
    {
        // No matter the (irrelevant) choice, an unmodified document is safe to discard.
        Assert.True(MainWindow.ShouldProceedAfterPrompt(isModified: false, MainWindow.SaveChoice.Cancel));
        Assert.True(MainWindow.ShouldProceedAfterPrompt(isModified: false, MainWindow.SaveChoice.DontSave));
        Assert.True(MainWindow.ShouldProceedAfterPrompt(isModified: false, MainWindow.SaveChoice.Save));
    }

    [Fact]
    public void Modified_document_proceeds_on_save_or_dont_save_but_not_cancel()
    {
        Assert.True(MainWindow.ShouldProceedAfterPrompt(isModified: true, MainWindow.SaveChoice.Save));
        Assert.True(MainWindow.ShouldProceedAfterPrompt(isModified: true, MainWindow.SaveChoice.DontSave));
        Assert.False(MainWindow.ShouldProceedAfterPrompt(isModified: true, MainWindow.SaveChoice.Cancel));
    }

    // ---- #5: Text-to-Columns warns only when the column to the right has data ----

    [Fact]
    public void HasDataToRight_is_false_when_the_next_column_is_empty()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Text("a,b,c"));
        var source = new CellRange(new CellAddress(0, 0), new CellAddress(0, 0));

        Assert.False(SpreadsheetEditorView.HasDataToRight(sheet, source));
    }

    [Fact]
    public void HasDataToRight_is_true_when_a_cell_to_the_right_holds_data()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Text("a,b,c"));
        sheet.SetValue(1, 1, CellValue.Text("existing")); // column B, within the source rows
        var source = new CellRange(new CellAddress(0, 0), new CellAddress(0, 2));

        Assert.True(SpreadsheetEditorView.HasDataToRight(sheet, source));
    }

    [Fact]
    public void HasDataToRight_ignores_data_outside_the_source_rows()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Text("a,b"));
        sheet.SetValue(9, 1, CellValue.Text("far below")); // column B but well below the source row
        var source = new CellRange(new CellAddress(0, 0), new CellAddress(0, 0));

        Assert.False(SpreadsheetEditorView.HasDataToRight(sheet, source));
    }

    // ---- Toggle Filter off clears the lingering filter spec and sort ----

    [AvaloniaFact]
    public void Toggling_the_filter_off_clears_the_filter_spec_and_sort()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("Header"));
        sheet.SetValue(1, 0, CellValue.Text("a"));
        sheet.SetValue(2, 0, CellValue.Text("b"));

        env.Selection.SetRange(0, 0, 2, 0);
        window.Editor.ToggleFilter(); // on
        Assert.NotNull(sheet.ActiveFilter);

        // Simulate conditions/sort having been applied while the filter was on.
        sheet.ActiveFilterSpec = new FilterSpec(new Dictionary<int, ColumnFilter>());
        sheet.ActiveSort = new SortSpec([new SortLevel(0)]);

        window.Editor.ToggleFilter(); // off

        Assert.Null(sheet.ActiveFilter);
        Assert.Null(sheet.ActiveFilterSpec);
        Assert.Null(sheet.ActiveSort);
    }
}
