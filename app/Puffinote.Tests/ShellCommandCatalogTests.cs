using Avalonia.Input;
using Puffinote.Components.Commands;
using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class ShellCommandCatalogTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-catalog");

    public void Dispose() => _dir.Delete(recursive: true);

    private ShortcutKeymap Keymap() => ShortcutKeymap.Load(System.IO.Path.Combine(_dir.FullName, "keymap.json"));

    [Fact]
    public void Every_superseded_key_is_itself_reserved()
    {
        // The supersedes set (⌘K / ⌘O, excluded from the conflict warning) must be a subset of the reserved
        // set — the shell can only "supersede" a key it actually reserves.
        Assert.All(ShellCommandCatalog.ShellSupersedesEditor, id => Assert.True(ShellCommandCatalog.IsReserved(id)));
    }

    [Fact]
    public void A_non_reserved_command_resolves_to_its_user_binding_when_the_keymap_sets_one()
    {
        var keymap = Keymap();
        var custom = Shortcut.Cmd(Key.J);
        keymap.Set("shell.save", custom);

        Assert.Equal(custom, ShellCommandCatalog.ResolveGesture(keymap, "shell.save", Shortcut.Cmd(Key.S)));
    }

    [Fact]
    public void A_non_reserved_command_falls_back_to_its_default_when_the_keymap_is_silent()
    {
        var def = Shortcut.Cmd(Key.S);
        Assert.Equal(def, ShellCommandCatalog.ResolveGesture(Keymap(), "shell.save", def));
    }

    [Fact]
    public void A_reserved_command_keeps_its_fixed_default_even_when_the_keymap_rebinds_it()
    {
        var keymap = Keymap();
        keymap.Set("shell.commandPalette", Shortcut.Cmd(Key.P)); // a stray custom binding on a reserved id

        // Reserved keys are fixed (PRD §7.6): resolution ignores the keymap and returns the default gesture.
        var def = Shortcut.Cmd(Key.K);
        Assert.Equal(def, ShellCommandCatalog.ResolveGesture(keymap, "shell.commandPalette", def));
    }
}
