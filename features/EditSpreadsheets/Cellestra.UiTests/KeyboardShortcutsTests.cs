using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Cellestra.Desktop.Views.Dialogs;
using Puffinote.Components.Commands;
using Xunit;

namespace Cellestra.UiTests;

public class KeyboardShortcutsTests
{
    [Fact]
    public void Help_command_is_bound_to_question_mark()
    {
        // Metadata only — build the catalog against a no-op environment (no MainWindow needed).
        var command = FakeAppEnvironment.Catalog.ById("help.shortcuts");
        Assert.NotNull(command);
        Assert.Equal(new KeyGesture(Key.OemQuestion, KeyModifiers.Shift), command!.Gesture);
        // Cellestra is macOS-only; assert the macOS rendering explicitly (the test host is Linux).
        Assert.Equal("?", ShortcutFormatter.Format(command.Shortcut!.Value, mac: true));
    }

    [AvaloniaFact]
    public void Help_command_is_disabled_while_a_text_field_has_focus()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        // Grid has focus by default: the "?" shortcut is live.
        Assert.False(env.IsTextInputFocused);
        Assert.True(env.Commands.ById("help.shortcuts")!.IsEnabled);

        // While editing a cell the character must type normally, so the shortcut backs off.
        env.BeginEdit();
        Assert.True(env.IsTextInputFocused);
        Assert.False(env.Commands.ById("help.shortcuts")!.IsEnabled);
    }

    [AvaloniaFact]
    public void Shortcuts_dialog_constructs()
    {
        // Built from the one catalog; constructing it exercises the section/row layout. The catalog's
        // metadata is env-independent, so a no-op environment suffices (no MainWindow needed).
        var dialog = new KeyboardShortcutsDialog(FakeAppEnvironment.Catalog);
        Assert.Equal("Keyboard Shortcuts", dialog.Title);
    }
}
