using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// While a modal (or a sub-modal layered inside one) is open, Tab must cycle only within that overlay —
/// it must never reach the grid, toolbar, or the dialog body behind it.
/// </summary>
public class ModalFocusTrapTests
{
    [AvaloniaFact]
    public void Tab_is_trapped_within_the_command_palette()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        env.ShowCommandPalette();
        Dispatcher.UIThread.RunJobs(); // run the palette's deferred focus-into-the-search-box

        var overlay = window.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "OverlayHost");
        var focusManager = ((TopLevel)window).FocusManager!;

        // Tab a few times — more than the palette has stops — and focus must never leave the overlay.
        for (int i = 0; i < 6; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            var focused = focusManager.GetFocusedElement() as Visual;
            Assert.NotNull(focused);
            Assert.True(overlay.IsVisualAncestorOf(focused!), "Tab escaped the palette overlay");
            Assert.False(window.Editor.Grid.IsFocused);
        }
    }

    [AvaloniaFact]
    public void Tab_is_trapped_within_a_dialog_sub_modal()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });
        var window = new Window { Width = 600, Height = 600, Content = dialog };
        window.Show();

        dialog.OpenCommandPickerForTest(1); // a sub-modal layered over the dialog body
        Dispatcher.UIThread.RunJobs();

        var picker = dialog.CommandPickerOverlayForTest;
        var focusManager = ((TopLevel)window).FocusManager!;

        for (int i = 0; i < 5; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            var focused = focusManager.GetFocusedElement() as Visual;
            Assert.NotNull(focused);
            Assert.True(picker.IsVisualAncestorOf(focused!), "Tab escaped the wedge command picker");
        }
    }
}
