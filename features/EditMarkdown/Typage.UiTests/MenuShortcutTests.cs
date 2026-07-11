using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The window registers one key binding per command using the platform gesture (Control on this Linux
/// test host, ⌘ on macOS), and the standard text-editing chords are left to the focused editor / field
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
        var window = new MainWindow(new SettingsService());
        // On the (non-mac) test host the command modifier is Control.
        Assert.True(HasBinding(window, Key.N, KeyModifiers.Control));                       // New
        Assert.True(HasBinding(window, Key.S, KeyModifiers.Control));                       // Save
        Assert.True(HasBinding(window, Key.S, KeyModifiers.Control | KeyModifiers.Shift));  // Save As
        Assert.True(HasBinding(window, Key.F, KeyModifiers.Control));                       // Find
        Assert.True(HasBinding(window, Key.F, KeyModifiers.Control | KeyModifiers.Alt));    // Find & Replace
        Assert.True(HasBinding(window, Key.F11, KeyModifiers.None));                        // Focus mode
        Assert.True(HasBinding(window, Key.D, KeyModifiers.Control));                        // Duplicate Line
        Assert.True(HasBinding(window, Key.K, KeyModifiers.Control | KeyModifiers.Shift));   // Delete Line
        Assert.True(HasBinding(window, Key.D6, KeyModifiers.Control));                       // Heading 6
        Assert.True(HasBinding(window, Key.E, KeyModifiers.Control | KeyModifiers.Shift));   // Code Block
        Assert.True(HasBinding(window, Key.H, KeyModifiers.Control | KeyModifiers.Shift));   // Highlight
        Assert.True(HasBinding(window, Key.OemPeriod, KeyModifiers.Control));                // Bulleted List
        Assert.True(HasBinding(window, Key.OemQuestion, KeyModifiers.Control));              // Numbered List
    }

    [AvaloniaFact]
    public void CommandPalette_TakesCmdK_AndInsertLink_MovesOffIt()
    {
        // ⌘K is the palette in Puffinote, where it's a reserved global key. The standalone app now agrees —
        // it used to bind ⌘K to Insert Link and put the palette on ⌘⇧P, so the same chord did two different
        // things depending on which app the editor was in.
        var window = new MainWindow(new SettingsService());
        Assert.True(HasBinding(window, Key.K, KeyModifiers.Control));                        // Command Palette
        Assert.True(HasBinding(window, Key.K, KeyModifiers.Control | KeyModifiers.Alt));     // Insert Link
        Assert.False(HasBinding(window, Key.P, KeyModifiers.Control | KeyModifiers.Shift));  // the old palette key
    }

    [AvaloniaFact]
    public void EditingChords_AreNotReboundAtTheWindow()
    {
        var window = new MainWindow(new SettingsService());
        // ⌘/Ctrl + C / A / Z stay with the focused editor or dialog text field.
        Assert.False(HasBinding(window, Key.C, KeyModifiers.Control));
        Assert.False(HasBinding(window, Key.A, KeyModifiers.Control));
        Assert.False(HasBinding(window, Key.Z, KeyModifiers.Control));
    }
}
