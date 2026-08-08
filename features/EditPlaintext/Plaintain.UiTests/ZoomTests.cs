using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Plaintain.Control.Views;
using Puffinote.Components.Commands;
using Puffinote.Components.Input;
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

    /// <summary>
    /// Raise the gesture macOS reports for a trackpad pinch. Headless input has no way to send one — it is a
    /// platform gesture, not a synthesised mouse move — so the event goes in where the backend would put it.
    /// </summary>
    private static void Magnify(PlainTextEditorView target, double magnification, ulong timestampMs) =>
        target.RaiseEvent(new PointerDeltaEventArgs(
            InputElement.PointerTouchPadGestureMagnifyEvent,
            target,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            TopLevel.GetTopLevel(target)!,
            default,
            timestampMs,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            new Vector(magnification, magnification)));

    [AvaloniaFact]
    public void A_trackpad_pinch_lands_on_the_same_stops_the_keys_do()
    {
        var (view, _) = Open();

        // A pinch arrives as a drizzle of increments, and one of them is not a zoom: the gesture gathers
        // until it is worth a whole stop, so the reader never gets a size that isn't on the ladder.
        Magnify(view, PinchZoom.MagnificationPerStop / 3, 100);
        Assert.Equal(ZoomScale.Default, view.Zoom);

        Magnify(view, PinchZoom.MagnificationPerStop / 3, 120);
        Magnify(view, PinchZoom.MagnificationPerStop / 3, 140);
        Assert.Equal(1.1, view.Zoom); // exactly where ⌘= would have put it

        Magnify(view, -PinchZoom.MagnificationPerStop, 160);
        Assert.Equal(ZoomScale.Default, view.Zoom);
    }

    [AvaloniaFact]
    public void A_pinch_that_can_no_longer_zoom_stops_telling_the_host_it_did()
    {
        var (view, _) = Open();
        view.Zoom = ZoomScale.Max;

        int changes = 0;
        view.ZoomChanged += (_, _) => changes++;
        Magnify(view, PinchZoom.MagnificationPerStop, 100);

        // Hard against 400%. A host writes its settings file on this event, and pinching at the end of the
        // ladder must not keep rewriting it with the number that is already there.
        Assert.Equal(0, changes);
        Assert.Equal(ZoomScale.Max, view.Zoom);
    }

    [AvaloniaFact]
    public void Ctrl_wheel_zooms_the_log_where_a_bare_wheel_scrolls_it()
    {
        var (view, window) = Open(string.Join('\n', Enumerable.Range(0, 400).Select(i => $"line {i}")));
        Assert.Equal(0, view.Editor.Offset.Y);

        // Windows and X11 report a touchpad pinch as ctrl+wheel. The editor handles the wheel itself and
        // marks it handled, so this only zooms if the gesture is seen on the way down rather than up.
        window.MouseWheel(new Point(60, 60), new Vector(0, -1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.9, view.Zoom);
        Assert.Equal(0, view.Editor.Offset.Y);

        // Same wheel without the modifier: still a scroll, and not a zoom.
        window.MouseWheel(new Point(60, 60), new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.9, view.Zoom);
        Assert.True(view.Editor.Offset.Y > 0, "a bare wheel should still scroll the log");
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
