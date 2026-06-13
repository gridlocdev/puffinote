using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Delete must forward-delete one character while editing a cell or the formula bar, and clear the
/// selection only in selection mode. The macOS trap (Avalonia #7133): a native menu item's Gesture is
/// a system key equivalent that's claimed ahead of the focused control and is swallowed even when the
/// item is disabled — so a plain-<c>Delete</c> "Clear Contents" equivalent eats the key mid-edit and
/// the TextBox never forward-deletes. The fix: plain-key commands carry no native key equivalent; the
/// grid's own key handling and the editors drive Delete instead.
/// </summary>
public class DeleteKeyClearContentsTests
{
    private static NativeMenuItem MenuItem(MainWindow window, string submenu, string label)
    {
        var menu = NativeMenu.GetMenu(window)!;
        var sub = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == submenu).Menu!;
        return sub.Items.OfType<NativeMenuItem>().Single(i => i.Header == label);
    }

    private static NativeMenuItem ClearContentsItem(MainWindow window) => MenuItem(window, "Edit", "Clear Contents");

    [AvaloniaFact]
    public void Plain_key_menu_item_has_no_native_key_equivalent_but_modifier_items_keep_theirs()
    {
        var window = new MainWindow();
        window.Show();

        // Clear Contents (plain Delete) must NOT register a macOS key equivalent — otherwise the system
        // swallows Delete while a cell/formula bar is being edited, blocking the TextBox's forward-delete.
        Assert.Null(ClearContentsItem(window).Gesture);

        // Modifier shortcuts (⌘C) are safe — they keep their equivalent so the menu shows the hint.
        Assert.NotNull(MenuItem(window, "Edit", "Copy").Gesture);
    }

    [AvaloniaFact]
    public void Native_menu_clear_contents_is_disabled_while_the_formula_bar_is_focused()
    {
        var window = new MainWindow();
        window.Show();
        window.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("hello"));
        window.Selection.MoveTo(0, 0);
        window.Refresh();

        var clear = ClearContentsItem(window);

        // Grid focused (not editing): Delete clears the cell — the menu command is live.
        window.Grid.Focus();
        Assert.True(clear.Command!.CanExecute(null));

        // Formula bar focused for editing: the same key equivalent must back off so the keypress
        // falls through to the TextBox and forward-deletes one character instead of wiping the cell.
        window.FormulaInput.Focus();
        Assert.False(clear.Command!.CanExecute(null));
    }

    [AvaloniaFact]
    public void Native_menu_clear_contents_is_disabled_while_the_cell_editor_is_focused()
    {
        var window = new MainWindow();
        window.Show();
        window.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("hello"));
        window.Selection.MoveTo(0, 0);
        window.Refresh();

        window.Grid.Focus();
        window.Grid.BeginEdit(null); // inline cell editor takes focus

        var clear = ClearContentsItem(window);
        Assert.False(clear.Command!.CanExecute(null));
    }

    [AvaloniaFact]
    public void Clear_contents_yields_while_an_inline_edit_is_active_even_without_textbox_focus()
    {
        var window = new MainWindow();
        window.Show();
        window.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("hello"));
        window.Selection.MoveTo(0, 0);
        window.Refresh();

        // The formula-bar mirror seeds a focus-less inline edit (BeginEdit(focus: false)). That yields
        // the real "editing, but no TextBox holds focus" state — the editing flag alone must keep the
        // plain-Delete command inert, so focus-detection wobble can't let it wipe the cell mid-edit.
        window.Grid.Focus();
        window.Grid.SetInlineEditorText("hel");
        Assert.True(window.Grid.IsInlineEditing);
        Assert.False(window.IsTextInputFocused);

        var clear = ClearContentsItem(window);
        Assert.False(clear.Command!.CanExecute(null));
    }

    // ---- end-to-end key path -------------------------------------------------

    private static TextBox Editor(SpreadsheetGrid grid) =>
        (TextBox)typeof(SpreadsheetGrid)
            .GetField("_editor", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(grid)!;

    [AvaloniaFact]
    public void Delete_clears_the_cell_in_selection_mode()
    {
        var window = new MainWindow();
        window.Show();
        window.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("hello"));
        window.Selection.MoveTo(0, 0);
        window.Refresh();
        window.Grid.Focus();

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.Equal(string.Empty, window.Session.ActiveSheet.Find(0, 0)?.Value.ToInvariantString() ?? string.Empty);
    }

    [AvaloniaFact]
    public void Delete_forward_deletes_one_character_while_editing_a_cell()
    {
        var window = new MainWindow();
        window.Show();
        window.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("hello"));
        window.Selection.MoveTo(0, 0);
        window.Refresh();

        window.Grid.Focus();
        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None); // begin inline edit
        var editor = Editor(window.Grid);
        editor.CaretIndex = 1; // I-beam after 'h'

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.Equal("hllo", editor.Text); // 'e' removed, cell still uncommitted
        Assert.Equal("hello", window.Session.ActiveSheet.Find(0, 0)?.Value.ToInvariantString());
    }

    [AvaloniaFact]
    public void Delete_forward_deletes_one_character_in_the_formula_bar()
    {
        var window = new MainWindow();
        window.Show();
        window.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("hello"));
        window.Selection.MoveTo(0, 0);
        window.Refresh();

        window.FormulaInput.Focus();
        window.FormulaInput.Text = "hello";
        window.FormulaInput.CaretIndex = 1; // I-beam after 'h'
        window.FormulaInput.SelectionStart = window.FormulaInput.SelectionEnd = 1;

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.Equal("hllo", window.FormulaInput.Text);
    }
}
