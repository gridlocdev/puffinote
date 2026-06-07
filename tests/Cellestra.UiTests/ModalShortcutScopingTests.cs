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
    private static bool IsBold(MainWindow window)
    {
        var sheet = ((IAppEnvironment)window).Session.ActiveSheet;
        var sel = ((IAppEnvironment)window).Selection;
        var cell = sheet.Find(sel.ActiveRow, sel.ActiveColumn);
        var styleId = cell?.StyleId ?? StylePool.DefaultId;
        return window.Session.Workbook.Styles.Get(styleId).Font.Bold;
    }

    [AvaloniaFact]
    public void Opening_and_closing_the_palette_toggles_modal_state()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;

        Assert.False(env.IsModalOpen);

        env.ShowCommandPalette();
        Assert.True(env.IsModalOpen);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(env.IsModalOpen);
    }

    [AvaloniaFact]
    public void Command_shortcut_is_ignored_while_the_palette_is_open()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;
        env.Session.ActiveSheet.SetValue(0, 0, CellValue.Text("x"));
        env.Selection.MoveTo(0, 0);
        Assert.False(IsBold(window));

        // Palette open: ⌘B must not bold the cell behind it.
        env.ShowCommandPalette();
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Meta);
        Assert.False(IsBold(window));

        // Palette closed: the same shortcut works again.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Meta);
        Assert.True(IsBold(window));
    }
}
