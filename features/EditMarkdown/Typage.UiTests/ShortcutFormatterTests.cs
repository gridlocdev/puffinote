using Avalonia.Input;
using Puffinote.Components.Commands;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Platform-native shortcut rendering and binding (PRD §7.5): macOS uses ⌘ glyphs, other platforms use
/// "Ctrl+…" text, and the bound gesture carries Meta on macOS / Control elsewhere. The platform is passed
/// explicitly so both renderings are checkable from one (Linux) test host.
/// </summary>
public class ShortcutFormatterTests
{
    [Theory]
    [InlineData(Key.N, KeyModifiers.None, "⌘N", "Ctrl+N")]
    [InlineData(Key.S, KeyModifiers.Shift, "⇧⌘S", "Ctrl+Shift+S")]
    [InlineData(Key.F, KeyModifiers.Alt, "⌥⌘F", "Ctrl+Alt+F")]
    [InlineData(Key.D1, KeyModifiers.None, "⌘1", "Ctrl+1")]
    [InlineData(Key.OemComma, KeyModifiers.None, "⌘,", "Ctrl+,")]
    [InlineData(Key.OemPeriod, KeyModifiers.None, "⌘.", "Ctrl+.")]      // Bulleted List
    [InlineData(Key.OemQuestion, KeyModifiers.None, "⌘/", "Ctrl+/")]    // Numbered List
    public void Cmd_RendersGlyphsOnMac_AndTextElsewhere(Key key, KeyModifiers extra, string mac, string other)
    {
        var shortcut = Shortcut.Cmd(key, extra);
        Assert.Equal(mac, ShortcutFormatter.Format(shortcut, mac: true));
        Assert.Equal(other, ShortcutFormatter.Format(shortcut, mac: false));
    }

    [Fact]
    public void BareKey_HasNoModifier_OnEitherPlatform()
    {
        var f11 = Shortcut.Bare(Key.F11);
        Assert.Equal("F11", ShortcutFormatter.Format(f11, mac: true));
        Assert.Equal("F11", ShortcutFormatter.Format(f11, mac: false));
    }

    [Fact]
    public void ToGesture_UsesMetaOnMac_ControlElsewhere()
    {
        var save = Shortcut.Cmd(Key.S);
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Meta), save.ToGesture(mac: true));
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control), save.ToGesture(mac: false));

        var saveAs = Shortcut.Cmd(Key.S, KeyModifiers.Shift);
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift), saveAs.ToGesture(mac: true));

        Assert.Equal(new KeyGesture(Key.F11), Shortcut.Bare(Key.F11).ToGesture(mac: true));
    }

    [Theory]
    [InlineData(Key.C, KeyModifiers.None, true)]   // ⌘C — a text field must keep it
    [InlineData(Key.A, KeyModifiers.None, true)]   // ⌘A
    [InlineData(Key.Z, KeyModifiers.None, true)]   // ⌘Z
    [InlineData(Key.S, KeyModifiers.None, false)]  // ⌘S — safe to claim as a menu equivalent
    [InlineData(Key.F, KeyModifiers.Alt, false)]   // ⌥⌘F — not a plain editing chord
    public void YieldsToTextInput_OnlyForStandardEditingChords(Key key, KeyModifiers extra, bool yields)
    {
        Assert.Equal(yields, Shortcut.Cmd(key, extra).YieldsToTextInput);
        Assert.False(Shortcut.Bare(Key.F11).YieldsToTextInput);
    }
}
