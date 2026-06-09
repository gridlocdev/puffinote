using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Core.Editing;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Covers the window-title format (#15/#16), the focused-text-input guard for global key bindings (#8),
/// the Reset Sheet Data command (#12), and the new-sheet formula-bar reset (#7).
/// </summary>
public class MainWindowChromeTests
{
    // ---- #15 / #16: title is just the document name (no "— Cellestra" suffix) ----

    [Fact]
    public void Title_for_a_new_workbook_is_Untitled()
    {
        Assert.Equal("Untitled", MainWindow.ComposeTitle(filePath: null, modified: false));
    }

    [Fact]
    public void Title_shows_only_the_file_name()
    {
        Assert.Equal("Budget.ods", MainWindow.ComposeTitle("/home/me/sheets/Budget.ods", modified: false));
    }

    [Fact]
    public void Title_prefixes_a_modified_indicator()
    {
        Assert.Equal("● Budget.ods", MainWindow.ComposeTitle("/home/me/Budget.ods", modified: true));
        Assert.Equal("● Untitled", MainWindow.ComposeTitle(null, modified: true));
    }

    // ---- #8: plain-key cell commands yield to a focused text input ----

    [Fact]
    public void Plain_Delete_yields_to_a_focused_text_input()
    {
        var delete = new KeyGesture(Key.Delete);
        Assert.True(MainWindow.ShouldYieldToTextInput(delete, textInputFocused: true));
        Assert.False(MainWindow.ShouldYieldToTextInput(delete, textInputFocused: false));
    }

    [Fact]
    public void Modifier_shortcuts_still_fire_while_a_text_input_is_focused()
    {
        var save = new KeyGesture(Key.S, KeyModifiers.Meta);
        var copy = new KeyGesture(Key.C, KeyModifiers.Meta);
        Assert.False(MainWindow.ShouldYieldToTextInput(save, textInputFocused: true));
        Assert.False(MainWindow.ShouldYieldToTextInput(copy, textInputFocused: true));
    }

    [Fact]
    public void Commands_without_a_shortcut_are_never_blocked()
    {
        Assert.False(MainWindow.ShouldYieldToTextInput(gesture: null, textInputFocused: true));
    }

    // ---- #12: Reset Sheet Data clears values and formatting, undoably ----

    [AvaloniaFact]
    public void Reset_sheet_data_clears_values_and_formatting()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        var sheet = env.Session.ActiveSheet;

        env.Session.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Text("hello")));
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, sheet.Name,
            new Cellestra.Core.Model.CellRange(new Cellestra.Core.Model.CellAddress(0, 0), new Cellestra.Core.Model.CellAddress(0, 0)),
            s => s.WithFont(s.Font.WithBold(true)), "Bold"));

        window.ResetSheetData();

        Assert.True(sheet.GetValue(0, 0).IsEmpty);
        var styleId = sheet.Find(0, 0)?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId;
        Assert.Equal(Cellestra.Core.Styling.StylePool.DefaultId, styleId);
        Assert.True(env.Session.CanUndo); // undoable
    }

    [AvaloniaFact]
    public void Reset_sheet_data_command_is_in_the_catalog()
    {
        var window = new MainWindow();
        window.Show();
        var command = AppCommands.ById("sheet.resetData");
        Assert.NotNull(command);
        Assert.True(command!.IsEnabled(window));
    }

    // ---- #7: a new sheet resets the active cell and clears the formula bar ----

    [AvaloniaFact]
    public void Adding_a_sheet_resets_the_active_cell_to_A1()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;

        env.Selection.MoveTo(5, 3); // move away from A1 on the first sheet
        window.AddSheet();

        Assert.Equal(0, env.Selection.ActiveRow);
        Assert.Equal(0, env.Selection.ActiveColumn);
        Assert.True(env.Session.ActiveSheet.GetValue(0, 0).IsEmpty); // the new sheet's A1 is empty
    }
}
