using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// While a modal overlay (Command Palette / sheet switcher) is open, the grid behind it must ignore
/// command shortcuts — only the overlay's own keys apply (e.g. ⌘A selects the palette text rather
/// than every cell).
/// </summary>
public class ModalShortcutScopingTests
{
    // The command modifier is ⌘ (Meta) on macOS and Ctrl elsewhere — mirror Shortcut.ModifiersFor so the
    // gesture we press matches the one the app actually binds. Hardcoding Meta makes ⌘-shortcuts a no-op on
    // Linux/Windows (the binding is Ctrl there), which silently turned these into vacuous tests — and broke
    // the one that asserts a shortcut fires again after the palette closes.
    private static readonly RawInputModifiers Cmd =
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static bool IsBold(MainWindow window)
    {
        var sheet = ((IAppEnvironment)window.Editor).Session.ActiveSheet;
        var sel = ((IAppEnvironment)window.Editor).Selection;
        var cell = sheet.Find(sel.ActiveRow, sel.ActiveColumn);
        var styleId = cell?.StyleId ?? StylePool.DefaultId;
        return window.Editor.Session.Workbook.Styles.Get(styleId).Font.Bold;
    }

    [AvaloniaFact]
    public void Opening_and_closing_the_palette_toggles_modal_state()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        Assert.False(env.IsModalOpen);

        env.ShowCommandPalette();
        Assert.True(env.IsModalOpen);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(env.IsModalOpen);
    }

    [AvaloniaFact]
    public void Grid_navigation_is_blocked_while_a_modal_is_open()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        env.Selection.MoveTo(0, 0);
        Assert.False(window.Editor.Grid.InputBlocked);

        env.ShowCommandPalette();
        Assert.True(window.Editor.Grid.InputBlocked);

        // Even with focus forced back onto the grid behind the palette, arrow keys must not move the
        // selection (they previously leaked to the hidden grid). The key is left for the overlay.
        window.Editor.Grid.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(0, env.Selection.ActiveRow);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(window.Editor.Grid.InputBlocked);

        // With the overlay gone, navigation works again.
        window.Editor.Grid.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(1, env.Selection.ActiveRow);
    }

    [AvaloniaFact]
    public void Select_all_does_not_expand_the_grid_while_the_palette_is_open()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        env.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("x"));
        env.Selection.MoveTo(0, 0);

        env.ShowCommandPalette();
        window.KeyPressQwerty(PhysicalKey.A, Cmd);

        // ⌘A belongs to the palette, not the grid behind it — the selection stays a single cell.
        Assert.Equal(0, env.Selection.Top);
        Assert.Equal(0, env.Selection.Bottom);
        Assert.Equal(0, env.Selection.Left);
        Assert.Equal(0, env.Selection.Right);
    }

    [AvaloniaFact]
    public void Command_shortcut_is_ignored_while_the_palette_is_open()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        env.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("x"));
        env.Selection.MoveTo(0, 0);
        Assert.False(IsBold(window));

        // Palette open: ⌘B must not bold the cell behind it.
        env.ShowCommandPalette();
        window.KeyPressQwerty(PhysicalKey.B, Cmd);
        Assert.False(IsBold(window));

        // Palette closed: the same shortcut works again.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.B, Cmd);
        Assert.True(IsBold(window));
    }
}
