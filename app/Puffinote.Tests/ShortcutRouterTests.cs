using Avalonia.Input;
using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class ShortcutRouterTests
{
    private static (KeyGesture, string)[] Reserved => new[]
    {
        (new KeyGesture(Key.K, KeyModifiers.Meta), "shell.commandPalette"),
        (new KeyGesture(Key.O, KeyModifiers.Meta), "shell.quickOpen"),
        (new KeyGesture(Key.OemCloseBrackets, KeyModifiers.Meta | KeyModifiers.Shift), "shell.nextTab"),
    };

    [Fact]
    public void A_reserved_key_routes_to_its_shell_command()
    {
        Assert.Equal("shell.commandPalette", ShortcutRouter.Match(Reserved, Key.K, KeyModifiers.Meta));
        Assert.Equal("shell.nextTab", ShortcutRouter.Match(Reserved, Key.OemCloseBrackets, KeyModifiers.Meta | KeyModifiers.Shift));
    }

    [Fact]
    public void A_non_reserved_key_falls_through_to_the_editor()
    {
        Assert.Null(ShortcutRouter.Match(Reserved, Key.S, KeyModifiers.Meta)); // ⌘S isn't reserved
        Assert.Null(ShortcutRouter.Match(Reserved, Key.K, KeyModifiers.None)); // bare K
    }

    [Fact]
    public void Modifiers_must_match_exactly()
    {
        // ⌘⇧K is not ⌘K — an editor binding on ⌘⇧K is not shadowed by the reserved ⌘K.
        Assert.Null(ShortcutRouter.Match(Reserved, Key.K, KeyModifiers.Meta | KeyModifiers.Shift));
        Assert.Null(ShortcutRouter.Match(Reserved, Key.OemCloseBrackets, KeyModifiers.Meta)); // missing Shift
    }
}
