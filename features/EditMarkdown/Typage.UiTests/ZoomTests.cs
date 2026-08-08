using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;
using Typage.Control.Rendering;
using Typage.Control.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Zoom in the note editor. Typage has no single "scale" to turn — it renders from a theme — so zoom is
/// spelled as the theme re-issued at a larger body size, and these hold the two things that makes fragile:
/// the palettes are shared singletons that must never be mutated, and the zoom must survive a theme switch.
/// </summary>
public class ZoomTests
{
    private static AppCommand Command(MarkdownEditorView view, string id) =>
        view.EditingCommands.Single(c => c.Id == id);

    [AvaloniaFact]
    public void Zooming_scales_the_type_and_the_line_height_together()
    {
        var view = new MarkdownEditorView();
        double size = view.Editor.EditorTheme.BaseFontSize;
        double line = view.Editor.EditorTheme.LineHeight;

        view.Zoom = 1.5;

        Assert.Equal(size * 1.5, view.Editor.EditorTheme.BaseFontSize);
        Assert.Equal(line * 1.5, view.Editor.EditorTheme.LineHeight);

        // The heading scale is authored as multiples of the body size, so it has to follow on its own.
        Assert.Equal(MarkdownTheme.Default.HeadingSize(1) * 1.5, view.Editor.EditorTheme.HeadingSize(1));
    }

    [AvaloniaFact]
    public void Zooming_never_touches_the_shared_palettes()
    {
        // Dark/Light/HighContrast are singletons handed to every editor in the process. If zoom scaled one
        // in place, two notes open in Puffinote would resize each other.
        var view = new MarkdownEditorView { EditorTheme = MarkdownTheme.Light, Zoom = 2.0 };

        Assert.Equal(32, view.Editor.EditorTheme.BaseFontSize); // this editor did scale…
        Assert.Equal(16, MarkdownTheme.Light.BaseFontSize);     // …and the palette it came from did not
        Assert.Equal(26, MarkdownTheme.Light.LineHeight);
        Assert.Equal(16, MarkdownTheme.Dark.BaseFontSize);
    }

    [AvaloniaFact]
    public void Switching_theme_keeps_the_zoom_and_switching_zoom_keeps_the_theme()
    {
        var view = new MarkdownEditorView { Zoom = 1.25, EditorTheme = MarkdownTheme.Light };

        Assert.Same(MarkdownTheme.Light.Background, view.Editor.EditorTheme.Background);
        Assert.Equal(16 * 1.25, view.Editor.EditorTheme.BaseFontSize);

        view.Zoom = 1.5;
        Assert.Same(MarkdownTheme.Light.Background, view.Editor.EditorTheme.Background);
        Assert.Equal(16 * 1.5, view.Editor.EditorTheme.BaseFontSize);

        // What the host set is what the host reads back — the scaling is the editor's business.
        Assert.Same(MarkdownTheme.Light, view.EditorTheme);
    }

    [AvaloniaFact]
    public void The_catalog_steps_the_shared_ladder_and_resets_to_100_percent()
    {
        var view = new MarkdownEditorView();

        Command(view, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(1.1, view.Zoom);
        Command(view, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(1.25, view.Zoom);
        Command(view, ZoomCommands.ZoomOutId).Execute();
        Assert.Equal(1.1, view.Zoom);

        Command(view, ZoomCommands.ActualSizeId).Execute();
        Assert.Equal(ZoomScale.Default, view.Zoom);
        Assert.Equal(16, view.Editor.EditorTheme.BaseFontSize);
    }

    [AvaloniaFact]
    public void Zoom_out_stops_being_offered_at_the_bottom_of_the_ladder()
    {
        var view = new MarkdownEditorView { Zoom = ZoomScale.Min };
        Assert.False(Command(view, ZoomCommands.ZoomOutId).IsEnabled);
        Assert.True(Command(view, ZoomCommands.ZoomInId).IsEnabled);

        view.Zoom = ZoomScale.Max;
        Assert.False(Command(view, ZoomCommands.ZoomInId).IsEnabled);
    }

    [AvaloniaFact]
    public void The_window_answers_to_the_plus_key_with_and_without_shift()
    {
        // The menu spells Zoom In as ⌘+, but no keyboard has an unshifted "+" on the number row: the key is
        // ⌘= and the glyph is on ⌘⇧=. Someone pressing what the menu says must not find a dead key.
        var window = TestShell.NewWindow();
        window.Show();

        window.KeyPressQwerty(PhysicalKey.Equal, TestShell.RawCommand);
        Assert.Equal(1.1, window.EditorView.Zoom);

        window.KeyPressQwerty(PhysicalKey.Equal, TestShell.RawCommand | RawInputModifiers.Shift);
        Assert.Equal(1.25, window.EditorView.Zoom);

        window.KeyPressQwerty(PhysicalKey.Minus, TestShell.RawCommand);
        Assert.Equal(1.1, window.EditorView.Zoom);

        window.KeyPressQwerty(PhysicalKey.Digit0, TestShell.RawCommand | RawInputModifiers.Alt);
        Assert.Equal(ZoomScale.Default, window.EditorView.Zoom);
    }

    [AvaloniaFact]
    public void Cmd0_stays_the_last_rung_of_the_heading_ladder_and_reset_takes_alt_cmd0()
    {
        // ⌘1–⌘6 apply a heading level and ⌘0 removes it; zoom's reset steps aside to ⌥⌘0 rather than
        // taking a chord that already means something in a note.
        var view = new MarkdownEditorView();
        Assert.Equal(Shortcut.Cmd(Key.D0), Command(view, "format.paragraph").Shortcut);
        Assert.Equal(Shortcut.Cmd(Key.D0, KeyModifiers.Alt), Command(view, ZoomCommands.ActualSizeId).Shortcut);
    }

    [AvaloniaFact]
    public void Cmd0_strips_the_heading_while_alt_cmd0_resets_the_zoom()
    {
        var window = TestShell.NewWindow();
        window.Show();
        window.EditorView.LoadText("# Heading");
        window.EditorView.Editor.Focus();
        window.EditorView.Zoom = 1.5;

        // ⌘0 is the editor's own — it must not also reset the zoom.
        window.KeyPressQwerty(PhysicalKey.Digit0, TestShell.RawCommand);
        Assert.Equal("Heading", window.EditorView.Text);
        Assert.Equal(1.5, window.EditorView.Zoom);

        // ⌥⌘0 is zoom's — the control has to let it through rather than strip a heading again.
        window.KeyPressQwerty(PhysicalKey.Digit0, TestShell.RawCommand | RawInputModifiers.Alt);
        Assert.Equal(ZoomScale.Default, window.EditorView.Zoom);
        Assert.Equal("Heading", window.EditorView.Text);
    }
}
