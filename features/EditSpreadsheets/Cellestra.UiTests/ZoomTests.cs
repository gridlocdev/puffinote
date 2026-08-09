using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Views;
using Cellestra.Editor;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Zoom on the grid. Cellestra had it first; what these hold is that it now steps the family's shared
/// ladder rather than its own ±10%, and that the status bar's percentage can't lag the size on screen.
/// </summary>
public class ZoomTests
{
    private static AppCommand Command(MainWindow window, string id) =>
        window.Editor.Commands.All.Single(c => c.Id == id);

    /// <summary>Where <paramref name="zoom"/> sits on the shared ladder — the zoom slider's own coordinate.</summary>
    private static int StopIndex(double zoom) => ZoomScale.Stops.ToList().IndexOf(zoom);

    [AvaloniaFact]
    public void Zooming_steps_the_shared_ladder()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Zoom = ZoomScale.Default;

        Command(window, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(1.1, window.Editor.Grid.Zoom);
        Command(window, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(1.25, window.Editor.Grid.Zoom);

        Command(window, ZoomCommands.ZoomOutId).Execute();
        Assert.Equal(1.1, window.Editor.Grid.Zoom);

        Command(window, ZoomCommands.ActualSizeId).Execute();
        Assert.Equal(ZoomScale.Default, window.Editor.Grid.Zoom);
    }

    [AvaloniaFact]
    public void The_grid_holds_the_ladder_s_bounds()
    {
        var window = new MainWindow();
        window.Show();

        window.Editor.Zoom = 99;
        Assert.Equal(ZoomScale.Max, window.Editor.Grid.Zoom);

        window.Editor.Zoom = 0;
        Assert.Equal(ZoomScale.Min, window.Editor.Grid.Zoom);
    }

    /// <summary>
    /// The status bar's percentage is the zoom control as well as the readout: pressing it drops a slider
    /// over the ladder. The slider steps <em>stops</em>, so every position it can rest on is one ⌘= and ⌘−
    /// can reach — there is no such thing as a zoom the rest of the app cannot get back to.
    /// </summary>
    [AvaloniaFact]
    public void The_zoom_readout_opens_a_slider_over_the_ladder()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Zoom = ZoomScale.Default;

        var slider = window.Editor.ZoomSliderForTest;
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(ZoomScale.Stops.Count - 1, slider.Maximum);
        Assert.True(slider.IsSnapToTickEnabled);
        Assert.Equal(1, slider.TickFrequency);

        // It opens pointing at the zoom in force, not at wherever it was left.
        Assert.Equal(StopIndex(ZoomScale.Default), slider.Value);
    }

    [AvaloniaFact]
    public void Moving_the_slider_applies_that_stop_immediately()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Zoom = ZoomScale.Default;

        var slider = window.Editor.ZoomSliderForTest;
        slider.Value = StopIndex(2.0);

        Assert.Equal(2.0, window.Editor.Grid.Zoom); // no OK to press: the grid behind the flyout is the preview
    }

    [AvaloniaFact]
    public void Zooming_by_any_other_route_moves_the_slider_with_it()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Zoom = ZoomScale.Default;

        Command(window, ZoomCommands.ZoomInId).Execute();

        Assert.Equal(1.1, window.Editor.Grid.Zoom);
        Assert.Equal(StopIndex(1.1), window.Editor.ZoomSliderForTest.Value);
    }

    /// <summary>
    /// A factor from off the ladder — an older build's, or a hand-edited settings file — has to show
    /// somewhere rather than leaving the thumb parked at 50%.
    /// </summary>
    [AvaloniaFact]
    public void An_off_ladder_zoom_shows_as_its_nearest_stop()
    {
        Assert.Equal(StopIndex(1.25), SpreadsheetEditorView.NearestStopIndex(1.3));
        Assert.Equal(StopIndex(0.5), SpreadsheetEditorView.NearestStopIndex(0.1));
        Assert.Equal(StopIndex(4.0), SpreadsheetEditorView.NearestStopIndex(99));
    }

    [AvaloniaFact]
    public void Zooming_scales_what_the_grid_draws()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Zoom = ZoomScale.Default;
        window.CaptureRenderedFrame();

        var sheet = window.Editor.Session.ActiveSheet;
        double width = sheet.GetColumnWidth(0);

        window.Editor.Zoom = 2.0;
        window.CaptureRenderedFrame();

        // The underlying column is untouched — zoom is a lens, not an edit.
        Assert.Equal(width, sheet.GetColumnWidth(0));
        Assert.Equal(2.0, window.Editor.Grid.Zoom);
    }
}
