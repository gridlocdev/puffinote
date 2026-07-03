using Avalonia.Input;
using Puffinote.Components.Commands;
using Puffinote.Desktop.Commands;
using Xunit;

namespace Puffinote.Tests;

public sealed class ShortcutConflictScannerTests
{
    private static AppCommand Cmd(string id, string title, Shortcut? shortcut) =>
        new(id, title, "desc", shortcut, () => { });

    [Fact]
    public void Detects_an_editor_binding_that_collides_with_a_reserved_key()
    {
        var reserved = new[]
        {
            Cmd("shell.commandPalette", "Command Palette", Shortcut.Cmd(Key.K)),
            Cmd("shell.closeTab", "Close Tab", Shortcut.Cmd(Key.W)),
        };
        var editor = new[]
        {
            Cmd("sheet.palette", "Sheet Command Palette", Shortcut.Cmd(Key.K)), // collides with ⌘K
            Cmd("sheet.bold", "Bold", Shortcut.Cmd(Key.B)),                     // no collision
        };

        var conflicts = ShortcutConflictScanner.Find(reserved, editor);

        var conflict = Assert.Single(conflicts);
        Assert.Equal("Command Palette", conflict.Reserved.Title);
        Assert.Equal("Sheet Command Palette", conflict.Editor.Title);
    }

    [Fact]
    public void No_conflict_when_gestures_differ_or_the_editor_command_has_none()
    {
        var reserved = new[] { Cmd("shell.commandPalette", "Command Palette", Shortcut.Cmd(Key.K)) };
        var editor = new[]
        {
            Cmd("sheet.bold", "Bold", Shortcut.Cmd(Key.B)),
            Cmd("sheet.noKey", "No Shortcut", null),
        };

        Assert.Empty(ShortcutConflictScanner.Find(reserved, editor));
    }
}
