using Avalonia.Headless.XUnit;
using Cellestra.Core.Values;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Editing a cell inline (double-click) then clicking into the formula bar commits the edit via the
/// editor's blur. That blur-commit must not re-grab grid focus: doing so reentrantly during the focus
/// transfer to the formula bar corrupts Avalonia's focus manager, so a subsequent click on a cell
/// can't pull focus back and the formula bar keeps its focus state. The commit must leave focus on
/// the formula bar so that focusing the grid afterward actually takes.
/// </summary>
public class FormulaBarFocusReleaseTests
{
    [AvaloniaFact]
    public void Clicking_a_cell_releases_formula_bar_focus_after_an_inline_edit_was_committed_via_the_bar()
    {
        var window = new MainWindow();
        window.Show();
        window.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("hello"));
        window.Selection.MoveTo(0, 0);
        window.Refresh();

        // Double-click a cell: inline editor takes focus.
        window.Grid.BeginEdit(null);
        Assert.True(window.Grid.IsInlineEditing);

        // Click the formula bar: the editor blurs and commits, but focus settles on the bar.
        window.FormulaInput.Focus();
        Assert.True(window.FormulaInput.IsFocused);
        Assert.False(window.Grid.IsInlineEditing);

        // Click back to a cell: focus must actually move to the grid (this was the broken case — the
        // formula bar used to retain focus because the blur-commit had stolen focus reentrantly).
        window.Grid.Focus();
        Assert.True(window.Grid.IsFocused);
        Assert.False(window.FormulaInput.IsFocused);
    }
}
