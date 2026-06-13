using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Values;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The plain-<c>Delete</c> "Clear Contents" command must yield to a focused text input — both as an
/// in-window KeyBinding and as a native-menu key equivalent (macOS dispatches the latter globally,
/// ahead of the focused control). Otherwise Delete wipes the whole cell while the formula bar or the
/// inline cell editor is being edited, instead of forward-deleting one character.
/// </summary>
public class DeleteKeyClearContentsTests
{
    private static NativeMenuItem ClearContentsItem(MainWindow window)
    {
        var menu = NativeMenu.GetMenu(window)!;
        var edit = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Edit").Menu!;
        return edit.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Clear Contents");
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
}
