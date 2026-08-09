using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;

namespace Cellestra.Editor;

public partial class SpreadsheetEditorView
{
    private Slider? _zoomSlider;
    private TextBlock? _zoomSliderReadout;

    // Guards the two directions of the slider ↔ zoom mirror: pushing the current zoom into the slider
    // moves its Value, which would otherwise read as the user having dragged it.
    private bool _syncingZoomSlider;

    /// <summary>
    /// Hang the zoom picker off the status bar's percentage. The readout already said what the zoom was;
    /// this makes it the place you change it too, which is where a reader looks first — the alternative was
    /// knowing ⌘= or finding View in a menu.
    /// <para>
    /// The slider steps <em>indices</em> into <see cref="ZoomScale.Stops"/>, not percentages, which is what
    /// the request for "its existing snap points" amounts to: every position on the track is a stop the rest
    /// of the app can already reach, ⌘= from any of them lands on the next one, and the ladder's deliberately
    /// uneven spacing (tight around 100%, wide at the ends) becomes even travel under the thumb instead of a
    /// bunched-up left half.
    /// </para>
    /// </summary>
    private void BuildZoomMenu()
    {
        var palette = Tokens.Active;

        _zoomSliderReadout = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
        };

        _zoomSlider = new Slider
        {
            Minimum = 0,
            Maximum = ZoomScale.Stops.Count - 1,
            TickFrequency = 1,
            IsSnapToTickEnabled = true, // the thumb can only rest on a stop — there are no in-between zooms
            TickPlacement = TickPlacement.BottomRight,
            Width = 220,
        };

        // Every move applies, rather than waiting for a release or an OK: the grid behind the flyout is the
        // preview, and a zoom you cannot see while choosing it is one you have to choose twice.
        _zoomSlider.PropertyChanged += (_, change) =>
        {
            if (change.Property == RangeBase.ValueProperty)
            {
                ApplyZoomFromSlider();
            }
        };

        // The ladder's two ends, written under the track, so the range is readable without dragging to find it.
        var ends = new DockPanel { LastChildFill = false, Width = 220 };
        var min = EndLabel(ZoomScale.Min, palette);
        var max = EndLabel(ZoomScale.Max, palette);
        DockPanel.SetDock(min, Dock.Left);
        DockPanel.SetDock(max, Dock.Right);
        ends.Children.Add(min);
        ends.Children.Add(max);

        ZoomButton.Flyout = new Flyout
        {
            Placement = PlacementMode.Top,
            Content = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(8, 6),
                Children = { _zoomSliderReadout, _zoomSlider, ends },
            },
        };

        SyncZoomMenu();
    }

    private static TextBlock EndLabel(double zoom, ThemePalette palette) => new()
    {
        Text = ZoomScale.Format(zoom),
        FontSize = 11,
        Foreground = palette.MutedText,
    };

    /// <summary>
    /// Point the slider at the zoom in force, wherever it came from — ⌘=, a pinch, the restored setting.
    /// An off-ladder factor (an older build's 1.3, a hand-edited settings file) shows as the nearest stop
    /// rather than as no position at all; the readout still says the real percentage.
    /// </summary>
    private void SyncZoomMenu()
    {
        if (_zoomSlider is null)
        {
            return;
        }

        _syncingZoomSlider = true;
        _zoomSlider.Value = NearestStopIndex(Grid.Zoom);
        _syncingZoomSlider = false;

        _zoomSliderReadout?.Text = ZoomScale.Format(Grid.Zoom);
    }

    private void ApplyZoomFromSlider()
    {
        if (_syncingZoomSlider || _zoomSlider is null)
        {
            return;
        }

        int index = Math.Clamp((int)Math.Round(_zoomSlider.Value), 0, ZoomScale.Stops.Count - 1);
        Zoom = ZoomScale.Stops[index]; // the view's own setter, so the grid redraws and the chrome follows
    }

    /// <summary>The index of the stop closest to <paramref name="zoom"/>.</summary>
    internal static int NearestStopIndex(double zoom)
    {
        double target = ZoomScale.Clamp(zoom);
        int best = 0;
        for (int i = 1; i < ZoomScale.Stops.Count; i++)
        {
            if (Math.Abs(ZoomScale.Stops[i] - target) < Math.Abs(ZoomScale.Stops[best] - target))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>The zoom picker's slider (test seam — drag it as a reader would).</summary>
    internal Slider ZoomSliderForTest => _zoomSlider!;
}
