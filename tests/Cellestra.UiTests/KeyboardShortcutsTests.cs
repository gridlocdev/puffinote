using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

public class KeyboardShortcutsTests
{
    [Fact]
    public void Help_command_is_bound_to_question_mark()
    {
        var command = AppCommands.ById("help.shortcuts");
        Assert.NotNull(command);
        Assert.Equal(new KeyGesture(Key.OemQuestion, KeyModifiers.Shift), command!.Shortcut);
        Assert.Equal("?", command.ShortcutText);
    }

    [AvaloniaFact]
    public void Help_command_is_disabled_while_a_text_field_has_focus()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window;

        // Grid has focus by default: the "?" shortcut is live.
        Assert.False(env.IsTextInputFocused);
        Assert.True(AppCommands.ById("help.shortcuts")!.IsEnabled(env));

        // While editing a cell the character must type normally, so the shortcut backs off.
        env.BeginEdit();
        Assert.True(env.IsTextInputFocused);
        Assert.False(AppCommands.ById("help.shortcuts")!.IsEnabled(env));
    }

    [AvaloniaFact]
    public void Shortcuts_dialog_constructs()
    {
        // Built from the one catalog; constructing it exercises the section/row layout.
        var dialog = new KeyboardShortcutsDialog();
        Assert.Equal("Keyboard Shortcuts", dialog.Title);
    }
}
