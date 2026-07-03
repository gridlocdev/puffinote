using Avalonia.Input;
using Puffinote.Components.Commands;
using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class ShortcutKeymapTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-keymap");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Path => System.IO.Path.Combine(_dir.FullName, "keymap.json");

    [Fact]
    public void Resolve_falls_back_to_the_default_until_a_binding_is_set()
    {
        var keymap = ShortcutKeymap.Load(Path);

        Assert.Null(keymap.For("shell.save"));
        Assert.Equal(Shortcut.Cmd(Key.S), keymap.Resolve("shell.save", Shortcut.Cmd(Key.S)));
    }

    [Fact]
    public void Set_persists_a_custom_binding_that_a_reload_picks_up()
    {
        var keymap = ShortcutKeymap.Load(Path);
        keymap.Set("shell.save", Shortcut.Cmd(Key.J, KeyModifiers.Shift));

        var reloaded = ShortcutKeymap.Load(Path);
        Assert.Equal(Shortcut.Cmd(Key.J, KeyModifiers.Shift), reloaded.For("shell.save"));
        Assert.Equal(Shortcut.Cmd(Key.J, KeyModifiers.Shift), reloaded.Resolve("shell.save", Shortcut.Cmd(Key.S)));
        Assert.Contains("shell.save", reloaded.CustomizedIds);
    }

    [Fact]
    public void Set_null_clears_a_binding_back_to_the_default()
    {
        var keymap = ShortcutKeymap.Load(Path);
        keymap.Set("shell.focusTree", Shortcut.Cmd(Key.L));
        keymap.Set("shell.focusTree", null);

        var reloaded = ShortcutKeymap.Load(Path);
        Assert.Null(reloaded.For("shell.focusTree"));
        Assert.DoesNotContain("shell.focusTree", reloaded.CustomizedIds);
    }
}
