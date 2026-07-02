using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Core.Editing;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Cellestra.Editor;
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

    // ---- #8: cell commands that conflict with text editing yield to a focused text input ----

    [Fact]
    public void Plain_Delete_yields_to_a_focused_text_input()
    {
        var delete = new KeyGesture(Key.Delete);
        Assert.True(SpreadsheetEditorView.ShouldYieldToTextInput(delete, textInputFocused: true));
        Assert.False(SpreadsheetEditorView.ShouldYieldToTextInput(delete, textInputFocused: false));
    }

    [Theory]
    // The standard text-editing chords (select-all, copy, cut, paste, undo, redo) yield to a focused
    // text field — so ⌘A selects the text being edited, not every cell — but only when focused.
    [InlineData(Key.A, KeyModifiers.Meta)]
    [InlineData(Key.C, KeyModifiers.Meta)]
    [InlineData(Key.X, KeyModifiers.Meta)]
    [InlineData(Key.V, KeyModifiers.Meta)]
    [InlineData(Key.Z, KeyModifiers.Meta)]
    [InlineData(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift)]
    public void Text_editing_chords_yield_to_a_focused_text_input(Key key, KeyModifiers mods)
    {
        var gesture = new KeyGesture(key, mods);
        Assert.True(SpreadsheetEditorView.ShouldYieldToTextInput(gesture, textInputFocused: true));
        Assert.False(SpreadsheetEditorView.ShouldYieldToTextInput(gesture, textInputFocused: false));
    }

    [Theory]
    // Modifier shortcuts that are NOT text-field gestures keep firing even while editing: Save, Bold,
    // and the sibling chords sharing a letter with an editing chord — ⌘⇧X Strikethrough (vs ⌘X Cut),
    // ⌘⇧C Format Painter (vs ⌘C Copy), ⌘⌥V Paste Special (vs ⌘V Paste).
    [InlineData(Key.S, KeyModifiers.Meta)]
    [InlineData(Key.B, KeyModifiers.Meta)]
    [InlineData(Key.X, KeyModifiers.Meta | KeyModifiers.Shift)]
    [InlineData(Key.C, KeyModifiers.Meta | KeyModifiers.Shift)]
    [InlineData(Key.V, KeyModifiers.Meta | KeyModifiers.Alt)]
    public void Non_text_modifier_shortcuts_still_fire_while_a_text_input_is_focused(Key key, KeyModifiers mods)
    {
        Assert.False(SpreadsheetEditorView.ShouldYieldToTextInput(new KeyGesture(key, mods), textInputFocused: true));
    }

    [Fact]
    public void Commands_without_a_shortcut_are_never_blocked()
    {
        Assert.False(SpreadsheetEditorView.ShouldYieldToTextInput(gesture: null, textInputFocused: true));
    }

    // ---- launch focus: the grid is keyboard-ready without a priming click ----

    [AvaloniaFact]
    public void The_grid_takes_focus_when_the_window_opens()
    {
        // OnOpened focuses the grid (and activates the window) so the keyboard works from launch
        // instead of the first interaction being spent activating an inactive macOS window.
        var window = new MainWindow();
        window.Show();

        Assert.True(window.Editor.Grid.IsFocused);
    }

    // ---- #12: Reset Sheet Data clears values and formatting, undoably ----

    [AvaloniaFact]
    public void Reset_sheet_data_clears_values_and_formatting()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Session.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Text("hello")));
        env.Session.Execute(EditCommands.ApplyStyle(env.Session.Workbook, sheet.Name,
            new Cellestra.Core.Model.CellRange(new Cellestra.Core.Model.CellAddress(0, 0), new Cellestra.Core.Model.CellAddress(0, 0)),
            s => s.WithFont(s.Font.WithBold(true)), "Bold"));

        window.Editor.ResetSheetData();

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
        var command = window.Editor.Commands.ById("sheet.resetData");
        Assert.NotNull(command);
        Assert.True(command!.IsEnabled);
    }

    // ---- #7: a new sheet resets the active cell and clears the formula bar ----

    [AvaloniaFact]
    public void Adding_a_sheet_resets_the_active_cell_to_A1()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        env.Selection.MoveTo(5, 3); // move away from A1 on the first sheet
        window.Editor.AddSheet();

        Assert.Equal(0, env.Selection.ActiveRow);
        Assert.Equal(0, env.Selection.ActiveColumn);
        Assert.True(env.Session.ActiveSheet.GetValue(0, 0).IsEmpty); // the new sheet's A1 is empty
    }
}
