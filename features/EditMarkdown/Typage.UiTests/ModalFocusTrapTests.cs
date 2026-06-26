using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Typage.Desktop.Commands;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// While the Command Palette overlay is open, Tab must cycle only within it — never reaching the editor
/// or toolbar behind it.
/// </summary>
public class ModalFocusTrapTests
{
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
