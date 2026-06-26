using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BirdBrain.Components.Overlays;
using Typage.Desktop.Commands;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// While an overlay (the Command Palette, or an internal modal dialog in the shared overlay host) is
/// open, Tab must cycle only within it — never reaching the editor or toolbar behind it.
/// </summary>
public class ModalFocusTrapTests
{
    [AvaloniaFact]
    public void Tab_is_trapped_within_a_confirm_modal_and_esc_cancels()
    {
        var window = new MainWindow(new SettingsService());
        window.Show();

        var host = window.GetVisualDescendants().OfType<OverlayHost>().First();
        var result = host.ShowModalAsync(new ConfirmDialog(
            heading: string.Empty,
            message: "Discard changes?",
            confirmLabel: "Discard",
            denyLabel: "Cancel",
            confirmIsDefault: false));
        Dispatcher.UIThread.RunJobs(); // settle focus into the dialog

        var focusManager = ((TopLevel)window).FocusManager!;
        for (int i = 0; i < 6; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            var focused = focusManager.GetFocusedElement() as Visual;
            Assert.NotNull(focused);
            Assert.True(host.IsVisualAncestorOf(focused!), "Tab escaped the confirm modal");
        }

        // Esc denies: the awaiter resolves false and the overlay tears down.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(result.IsCompleted);
        Assert.False(result.Result);
        Assert.False(host.IsOpen);
    }

    [AvaloniaFact]
    public void Tab_is_trapped_within_the_command_palette()
    {
        var window = new MainWindow(new SettingsService());
        window.Show();

        var palette = window.GetVisualDescendants().OfType<CommandPalette>().First();
        palette.Open(new List<AppCommand>
        {
            new("a", "Alpha", "First", null, () => { }),
            new("b", "Beta", "Second", null, () => { }),
            new("c", "Gamma", "Third", null, () => { }),
        });
        Dispatcher.UIThread.RunJobs(); // settle focus into the search box

        var focusManager = ((TopLevel)window).FocusManager!;

        // Tab more times than the palette has stops; focus must never leave the overlay.
        for (int i = 0; i < 6; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            var focused = focusManager.GetFocusedElement() as Visual;
            Assert.NotNull(focused);
            Assert.True(palette.IsVisualAncestorOf(focused!), "Tab escaped the command palette");
        }
    }
}
