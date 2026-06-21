using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// A Cmd/Ctrl chord is reserved for shortcuts. An unbound combo (e.g. ⌘J) must not type its character
/// into a cell — some platforms still emit a text-input event for it, which used to begin a cell edit.
/// </summary>
public class ModifierTypingTests
{
    [AvaloniaFact]
    public void Cmd_chord_does_not_begin_a_cell_edit()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Selection.MoveTo(0, 0);
        window.Editor.Grid.Focus();

        // ⌘J: the keydown carries the Meta modifier; even if a 'j' text-input follows, no edit starts.
        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Meta);
        window.KeyTextInput("j");

        Assert.False(window.Editor.Grid.IsInlineEditing);
        Assert.True(window.Editor.Session.ActiveSheet.GetValue(0, 0).IsEmpty);
    }

    [AvaloniaFact]
    public void Plain_key_still_begins_a_cell_edit()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Selection.MoveTo(0, 0);
        window.Editor.Grid.Focus();

        // No modifier: typing a character begins an inline edit as usual.
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.None);
        window.KeyTextInput("k");

        Assert.True(window.Editor.Grid.IsInlineEditing);
    }
}
