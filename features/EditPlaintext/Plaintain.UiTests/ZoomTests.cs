using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plaintain.Control.Views;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Zoom in the plaintext editor. It multiplies the configured font size rather than replacing it, which is
/// the property worth pinning: a log reader who set 16pt and then zoomed must land back on <i>their</i> 16
/// when they reset, not on the shipped default.
/// </summary>
public class ZoomTests
{
    private static (PlainTextEditorView View, Window Window) Open(string text = "alpha\nbeta\ngamma")
    {
        var view = new PlainTextEditorView();
        view.LoadText(text);
        var window = new Window { Width = 500, Height = 300, Content = view };
        window.Show();
        window.UpdateLayout();
        return (view, window);
    }

    private static AppCommand Command(PlainTextEditorView view, string id) =>
        view.EditingCommands.Single(c => c.Id == id);

    [AvaloniaFact]
    public void Zooming_grows_the_glyphs_and_the_rows_that_hold_them()
    {
        var (view, window) = Open();
        double row = view.Editor.RowHeight;
        double column = view.Editor.CharWidth;

        view.Zoom = 2.0;
        window.UpdateLayout();

        Assert.Equal(view.Editor.FontSize * 2.0, view.Editor.ScaledFontSize);
        Assert.True(view.Editor.RowHeight > row, "rows should grow with the text");
        Assert.True(view.Editor.CharWidth > column, "the monospace advance should grow with the text");
    }

    [AvaloniaFact]
    public void Zoom_multiplies_the_configured_size_rather_than_replacing_it()
    {
        var (view, window) = Open();
        view.Editor.FontSize = 20;
        view.Zoom = 1.5;
        window.UpdateLayout();
        Assert.Equal(30, view.Editor.ScaledFontSize);

        // Back to 100% is back to the reader's 20, not to the shipped 13.
        Command(view, ZoomCommands.ActualSizeId).Execute();
        Assert.Equal(20, view.Editor.ScaledFontSize);
        Assert.Equal(20, view.Editor.FontSize);
    }

    [AvaloniaFact]
    public void The_catalog_steps_the_shared_ladder_and_clamps_at_its_ends()
    {
        var (view, _) = Open();

        Command(view, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(1.1, view.Zoom);
        Command(view, ZoomCommands.ZoomOutId).Execute();
        Assert.Equal(ZoomScale.Default, view.Zoom);

        view.Zoom = 99;
        Assert.Equal(ZoomScale.Max, view.Zoom);
        view.Zoom = -1;
        Assert.Equal(ZoomScale.Min, view.Zoom);
    }

    [AvaloniaFact]
    public void A_host_is_told_when_the_zoom_changes_so_it_can_remember_it()
    {
        var (view, _) = Open();
        int changes = 0;
        view.ZoomChanged += (_, _) => changes++;

        Command(view, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(1, changes);

        view.Zoom = view.Zoom; // no change, no event — a host must not rewrite its settings file for nothing
        Assert.Equal(1, changes);
    }
}
