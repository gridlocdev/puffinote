using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Typage.Desktop.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The window registers one key binding per command using the platform gesture (⌘ on macOS, Control
/// elsewhere), and the standard text-editing chords are left to the focused editor / field
/// rather than re-bound at the window (PRD §7.5). The catalog it binds is the shell's own plus the editor's,
/// so every gesture here is one Puffinote binds too.
/// </summary>
public class MenuShortcutTests
{
    private static bool HasBinding(MainWindow window, Key key, KeyModifiers mods) =>
        window.KeyBindings.Any(b => b.Gesture is { } g && g.Key == key && g.KeyModifiers == mods);

    [AvaloniaFact]
    public void WindowCommands_AreBound_WithThePlatformModifier()
    {
        var window = TestShell.NewWindow();
        Assert.True(HasBinding(window, Key.N, TestShell.Command));                       // New
        Assert.True(HasBinding(window, Key.S, TestShell.Command));                       // Save
        Assert.True(HasBinding(window, Key.S, TestShell.Command | KeyModifiers.Shift));  // Save As
        Assert.True(HasBinding(window, Key.F, TestShell.Command));                       // Find
        Assert.True(HasBinding(window, Key.F, TestShell.Command | KeyModifiers.Alt));    // Find & Replace
        Assert.True(HasBinding(window, Key.F11, KeyModifiers.None));                        // Focus mode
        Assert.True(HasBinding(window, Key.D, TestShell.Command));                        // Duplicate Line
        Assert.True(HasBinding(window, Key.K, TestShell.Command | KeyModifiers.Shift));   // Delete Line
        Assert.True(HasBinding(window, Key.D6, TestShell.Command));                       // Heading 6
        Assert.True(HasBinding(window, Key.E, TestShell.Command | KeyModifiers.Shift));   // Code Block
        Assert.True(HasBinding(window, Key.H, TestShell.Command | KeyModifiers.Shift));   // Highlight
        Assert.True(HasBinding(window, Key.OemPeriod, TestShell.Command));                // Bulleted List
        Assert.True(HasBinding(window, Key.OemQuestion, TestShell.Command));              // Numbered List
    }

    [AvaloniaFact]
    public void CommandPalette_TakesCmdK_AndInsertLink_MovesOffIt()
    {
        // ⌘K is the palette in Puffinote, where it's a reserved global key. The standalone app now agrees —
        // it used to bind ⌘K to Insert Link and put the palette on ⌘⇧P, so the same chord did two different
        // things depending on which app the editor was in.
        var window = TestShell.NewWindow();
        Assert.True(HasBinding(window, Key.K, TestShell.Command));                        // Command Palette
        Assert.True(HasBinding(window, Key.K, TestShell.Command | KeyModifiers.Alt));     // Insert Link
        Assert.False(HasBinding(window, Key.P, TestShell.Command | KeyModifiers.Shift));  // the old palette key
    }

    [AvaloniaFact]
    public void EditingChords_AreNotReboundAtTheWindow()
    {
        var window = TestShell.NewWindow();
        // ⌘/Ctrl + C / A / Z stay with the focused editor or dialog text field.
        Assert.False(HasBinding(window, Key.C, TestShell.Command));
        Assert.False(HasBinding(window, Key.A, TestShell.Command));
        Assert.False(HasBinding(window, Key.Z, TestShell.Command));
    }
}
