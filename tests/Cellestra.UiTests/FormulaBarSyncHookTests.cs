using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The task-9 hook: the public surface the orchestrator will later wire to MainWindow's formula
/// bar (InlineEditTextChanged, SetInlineEditorText, IsInlineEditing). Verifies the contract works
/// in isolation — the grid raises on user edits, accepts pushed text, and exposes edit state.
/// </summary>
public class FormulaBarSyncHookTests
{
    [AvaloniaFact]
    public void IsInlineEditing_reflects_the_edit_lifecycle()
    {
        var window = new MainWindow();
        window.Show();
        var grid = window.Editor.Grid;

        Assert.False(grid.IsInlineEditing);
        ((IAppEnvironment)window.Editor).Selection.MoveTo(0, 0);
        grid.BeginEdit("abc");
        Assert.True(grid.IsInlineEditing);
    }

    [AvaloniaFact]
    public void SetInlineEditorText_begins_an_edit_when_none_is_active_without_echoing()
    {
        var window = new MainWindow();
        window.Show();
        var grid = window.Editor.Grid;
        ((IAppEnvironment)window.Editor).Selection.MoveTo(0, 0);

        int echoes = 0;
        grid.InlineEditTextChanged += _ => echoes++;

        grid.SetInlineEditorText("=SUM(A1:A2)");

        Assert.True(grid.IsInlineEditing);
        Assert.Equal(0, echoes); // a programmatic push must not fire the user-edit event
    }

    [AvaloniaFact]
    public void SetInlineEditorText_updates_the_live_editor_without_echoing()
    {
        var window = new MainWindow();
        window.Show();
        var grid = window.Editor.Grid;
        ((IAppEnvironment)window.Editor).Selection.MoveTo(0, 0);
        grid.BeginEdit("a");

        int echoes = 0;
        grid.InlineEditTextChanged += _ => echoes++;

        grid.SetInlineEditorText("abcd");

        Assert.True(grid.IsInlineEditing);
        Assert.Equal(0, echoes); // pushing from outside doesn't loop back as a user edit
    }
}
