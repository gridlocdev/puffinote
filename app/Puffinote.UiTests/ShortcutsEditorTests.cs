using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

public sealed class ShortcutsEditorTests
{
    private static void ClickButton(Visual root, string content) =>
        root.GetVisualDescendants().OfType<Button>()
            .First(b => (b.Content as string) == content)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static ShortcutBinding[] Sample() =>
    [
        new ShortcutBinding("shell.save", "Save", "Puffinote", Shortcut.Cmd(Key.S), false, true, false),
        new ShortcutBinding("shell.commandPalette", "Command Palette", "Puffinote", Shortcut.Cmd(Key.K), true, false, false),
    ];

    // ---- The shared control ------------------------------------------------------------------------

    [AvaloniaFact]
    public void Rebinding_a_row_captures_the_next_chord()
    {
        string? id = null;
        Shortcut? captured = null;
        var view = new ShortcutsEditorView();
        view.Rebound += (i, s) =>
        {
            id = i;
            captured = s;
        };
        view.SetBindings(Sample());

        var window = new Window { Content = view };
        window.Show();

        ClickButton(view, "Rebind");
        Assert.True(view.IsCapturing);

        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control); // the platform command modifier here

        Assert.Equal("shell.save", id);
        Assert.Equal(Shortcut.Cmd(Key.J), captured);
        Assert.False(view.IsCapturing);
    }

    [AvaloniaFact]
    public void A_reserved_row_offers_no_rebind_button()
    {
        var view = new ShortcutsEditorView();
        view.SetBindings(Sample());
        var window = new Window { Content = view };
        window.Show();

        // Only the rebindable Save row has a Rebind button; the reserved palette row is fixed.
        Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Rebind");
    }

    [AvaloniaFact]
    public void A_customized_row_can_be_reset()
    {
        string? id = null;
        Shortcut? captured = Shortcut.Cmd(Key.S);
        var view = new ShortcutsEditorView();
        view.Rebound += (i, s) =>
        {
            id = i;
            captured = s;
        };
        view.SetBindings(
        [
            new ShortcutBinding("shell.save", "Save", "Puffinote", Shortcut.Cmd(Key.J), false, true, IsCustomized: true),
        ]);
        var window = new Window { Content = view };
        window.Show();

        ClickButton(view, "Reset");

        Assert.Equal("shell.save", id);
        Assert.Null(captured); // reset = clear the custom binding
    }

    [AvaloniaFact]
    public void A_chord_that_a_plain_keypress_would_type_is_ignored_during_capture()
    {
        var rebounds = 0;
        var view = new ShortcutsEditorView();
        view.Rebound += (_, _) => rebounds++;
        view.SetBindings(Sample());
        var window = new Window { Content = view };
        window.Show();

        ClickButton(view, "Rebind");

        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.None); // bare J would type "j"
        Assert.True(view.IsCapturing); // still waiting
        Assert.Equal(0, rebounds);

        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Shift); // Shift+J would type "J"
        Assert.True(view.IsCapturing);
        Assert.Equal(0, rebounds);

        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control); // a real chord → accepted
        Assert.False(view.IsCapturing);
        Assert.Equal(1, rebounds);
    }

    [AvaloniaFact]
    public void Capturing_a_chord_already_bound_to_another_command_is_refused()
    {
        var rebounds = 0;
        var view = new ShortcutsEditorView();
        view.Rebound += (_, _) => rebounds++;
        view.SetBindings(Sample()); // Save (⌘S, rebindable) + Command Palette (⌘K, reserved)
        var window = new Window { Content = view };
        window.Show();

        ClickButton(view, "Rebind"); // rebind Save
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control); // ⌘/Ctrl+K — already Command Palette

        Assert.True(view.IsCapturing); // refused → still waiting
        Assert.Equal(0, rebounds);

        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control); // a free chord → accepted
        Assert.False(view.IsCapturing);
        Assert.Equal(1, rebounds);
    }

    [AvaloniaFact]
    public void Escape_cancels_capture_without_rebinding()
    {
        var rebounds = 0;
        var view = new ShortcutsEditorView();
        view.Rebound += (_, _) => rebounds++;
        view.SetBindings(Sample());
        var window = new Window { Content = view };
        window.Show();

        ClickButton(view, "Rebind");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(view.IsCapturing);
        Assert.Equal(0, rebounds);
    }

    // ---- Wired into the shell ----------------------------------------------------------------------

    [AvaloniaFact]
    public void Rebinding_a_shell_command_takes_effect_live_and_can_be_reset()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-keymap-ui");
        try
        {
            var window = new MainWindow(new NotebookService());
            window.UseKeymapForTest(ShortcutKeymap.Load(Path.Combine(dir.FullName, "keymap.json")));
            window.Show();

            Assert.Equal(Shortcut.Cmd(Key.S), window.ShellShortcutForTest("shell.save"));

            window.RebindShellForTest("shell.save", Shortcut.Cmd(Key.J));
            Assert.Equal(Shortcut.Cmd(Key.J), window.ShellShortcutForTest("shell.save"));

            window.RebindShellForTest("shell.save", null);
            Assert.Equal(Shortcut.Cmd(Key.S), window.ShellShortcutForTest("shell.save")); // back to default
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void A_reserved_shell_key_cannot_be_rebound()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-keymap-reserved");
        try
        {
            var window = new MainWindow(new NotebookService());
            window.UseKeymapForTest(ShortcutKeymap.Load(Path.Combine(dir.FullName, "keymap.json")));
            window.Show();

            window.RebindShellForTest("shell.commandPalette", Shortcut.Cmd(Key.J));

            Assert.Equal(Shortcut.Cmd(Key.K), window.ShellShortcutForTest("shell.commandPalette")); // fixed
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_shortcuts_command_opens_the_editor_overlay()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        Assert.False(window.IsOverlayOpenForTest);
        window.InvokeShellForTest("shell.shortcuts");
        Assert.True(window.IsOverlayOpenForTest);
    }
}
