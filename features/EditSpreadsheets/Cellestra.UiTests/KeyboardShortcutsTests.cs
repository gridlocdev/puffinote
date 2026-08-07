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
    public void Help_command_is_bound_to_command_question_mark()
    {
        // Metadata only — build the catalog against a no-op environment (no MainWindow needed).
        var command = FakeAppEnvironment.Catalog.ById("help.shortcuts");
        Assert.NotNull(command);

        // ⌘⇧/ — the command modifier is the point: a bare "?" is a character you type into a cell, and the
        // grid is exactly where you'd type it, so no focus gate could keep the two apart.
        Assert.True(command!.Shortcut!.Value.UsesCommand);
        Assert.Equal(new KeyGesture(Key.OemQuestion, KeyModifiers.Shift | KeyModifiers.Meta), command.Shortcut!.Value.ToGesture(mac: true));

        // Cellestra is macOS-only; assert the macOS rendering explicitly (the test host is Linux).
        Assert.Equal("⌘?", ShortcutFormatter.Format(command.Shortcut!.Value, mac: true));
    }

    [AvaloniaFact]
    public void Help_command_stays_available_while_a_text_field_has_focus()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        Assert.True(env.Commands.ById("help.shortcuts")!.IsEnabled);

        // It used to switch itself off mid-edit so the bare "?" could reach the cell. ⌘? has no such
        // conflict, so the reference stays reachable from wherever you are.
        env.BeginEdit();
        Assert.True(env.IsTextInputFocused);
        Assert.True(env.Commands.ById("help.shortcuts")!.IsEnabled);
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
