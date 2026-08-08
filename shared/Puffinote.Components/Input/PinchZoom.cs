using Avalonia.Input;
using Avalonia.Interactivity;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Input;

/// <summary>
/// Turns the drizzle of tiny increments a pinch produces into whole steps along the family's zoom ladder
/// (<see cref="ZoomScale"/>).
///
/// <para>A trackpad pinch is not one event: it arrives as dozens of fractional deltas, each far too small
/// to be worth a ladder stop on its own. So this accumulates them and reports a stop only once enough
/// travel has gathered — the same shape as <c>SidebarSwipe</c>, and for the same reason.</para>
///
/// <para>Two things end a gesture. A gap of <see cref="IdleResetMs"/> with no event: fingers left the
/// trackpad, so the leftover travel is dropped rather than carried into the next pinch minutes later. And a
/// reversal: pinching back is a new intent, not a cancellation of the old one, so it counts from zero and
/// answers after one stop's travel rather than after the residue plus one.</para>
/// </summary>
public sealed class PinchZoomGesture
{
    /// <summary>A gap this long (ms) with no event ends the gesture: drop the residue, start clean.</summary>
    public const ulong IdleResetMs = 250;

    private double _travel;
    private ulong _lastMs;

    /// <summary>
    /// Feed one event's worth of pinch, measured in ladder stops — <c>1.0</c> is one stop's worth of finger
    /// travel, positive is "bigger". <paramref name="timestampMs"/> is the event's own timestamp rather than
    /// a clock read, so the idle gap is measured in input time and stays deterministic under test.
    /// <para>Returns how many stops to step <i>now</i>: 0 while still gathering, negative to zoom out.</para>
    /// </summary>
    public int Feed(double stops, ulong timestampMs)
    {
        // A backwards timestamp shouldn't underflow into a 500-million-year gap; treat it as a fresh gesture.
        bool idle = timestampMs < _lastMs || timestampMs - _lastMs >= IdleResetMs;
        _lastMs = timestampMs;
        if (idle)
        {
            _travel = 0;
        }

        // A non-finite delta is a driver hiccup, not a pinch; a zero one is the tail of a gesture that has
        // stopped moving. Neither says anything about direction, so neither may reset the accumulation.
        if (!double.IsFinite(stops) || stops == 0)
        {
            return 0;
        }

        if (_travel != 0 && Math.Sign(_travel) != Math.Sign(stops))
        {
            _travel = 0;
        }

        _travel += stops;

        // Travel worth more than the entire ladder is a synthetic or corrupt delta. Honour its direction but
        // cap it — the caller walks the ladder a stop at a time, and 50% to 400% is only thirteen of them.
        // Capping here, before the cast, is also what keeps a wild double out of int's range.
        if (Math.Abs(_travel) >= ZoomScale.Stops.Count)
        {
            int capped = Math.Sign(_travel) * ZoomScale.Stops.Count;
            _travel = 0;
            return capped;
        }

        int steps = (int)_travel; // truncates toward zero, which is what "whole stops travelled" means
        _travel -= steps;
        return steps;
    }
}

/// <summary>
/// Pinch-to-zoom, wired once for the whole family. All four editors expose the same pair — a zoom factor to
/// read and one to write, on the shared <see cref="ZoomScale"/> ladder — so the gesture is written here
/// against that pair and each editor's view attaches it in one line, exactly as each one splices in
/// <see cref="Commands.ZoomCommands"/> over the same two lambdas.
///
/// <para><b>Where a trackpad pinch can actually be observed</b> depends on the platform, so this listens in
/// both places rather than picking one. macOS reports it as its own gesture
/// (<see cref="InputElement.PointerTouchPadGestureMagnify"/>, carrying an NSEvent magnification increment).
/// Windows and X11 have no such event: their drivers report a touchpad pinch as <b>ctrl+wheel</b>, which is
/// also what a mouse user presses to zoom, so the two arrive as one case and are handled as one.</para>
///
/// <para>A pinch lands on the <i>same stops</i> as ⌘= / ⌘−, rather than zooming continuously. One ladder for
/// the whole family is the point (see <see cref="ZoomScale"/>): a reader who pinches to something that
/// reads "137%" and then presses ⌘= has to be told what the next size even is, and every intermediate value
/// would re-lay out a whole spreadsheet on the way past. Stops keep the gesture and the keys speaking the
/// same language, and keep a 60Hz gesture to at most thirteen repaints.</para>
///
/// <para>Attaching is for the lifetime of the control; there is no detach, because an editor view that has
/// stopped wanting to zoom has stopped existing.</para>
/// </summary>
public static class PinchZoom
{
    /// <summary>
    /// How much accumulated macOS magnification is worth one ladder stop. Magnification is a ratio increment
    /// — Apple's own advice is to add 1.0 to it and multiply — and a comfortable full-hand pinch totals
    /// roughly 1.0, so this puts about six stops in a whole pinch: 100% reaches 300% without the reader
    /// running out of trackpad, and a small nudge still moves exactly one stop.
    /// </summary>
    public const double MagnificationPerStop = 0.15d;

    /// <summary>
    /// Wheel notches per ladder stop. One notch, one stop: that is a mouse's whole ctrl+wheel vocabulary,
    /// and a precision touchpad's fractional notches then accumulate into the same stops on the way.
    /// </summary>
    public const double NotchesPerStop = 1.0d;

    /// <summary>
    /// Make <paramref name="source"/> zoom to a trackpad pinch. <paramref name="get"/> reads the editor's
    /// current factor and <paramref name="set"/> applies a new one (the editor clamps and repaints) — the
    /// same seam <see cref="Commands.ZoomCommands.Build"/> takes, which is why one implementation serves
    /// four unrelated editing surfaces.
    /// </summary>
    public static void Attach(InputElement source, Func<double> get, Action<double> set)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        var gesture = new PinchZoomGesture();

        // macOS. Nothing else in the family wants this event, so a plain bubbling subscription is enough.
        source.PointerTouchPadGestureMagnify += (_, e) =>
        {
            // The backend puts the magnification on both axes; read whichever one arrived non-zero rather
            // than assuming an axis, since a pinch has no direction to speak of.
            double magnification = e.Delta.Y == 0 ? e.Delta.X : e.Delta.Y;
            Step(gesture.Feed(magnification / MagnificationPerStop, e.Timestamp), get, set);
            e.Handled = true;
        };

        // Windows/X11, and any mouse. Deliberately *tunnelling*: the surfaces underneath scroll on the wheel
        // and mark it handled (Plaintain's editor and Cellestra's grid both do), so a bubbling handler would
        // never see the gesture — it would scroll the document instead of zooming it.
        source.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

        void OnWheel(object? sender, PointerWheelEventArgs e)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                return; // an ordinary scroll — leave it to whatever it was aimed at
            }

            Step(gesture.Feed(e.Delta.Y / NotchesPerStop, e.Timestamp), get, set);

            // Handled even mid-gather: ctrl+wheel is a zoom whether or not this particular event completed a
            // stop, and letting the sub-notch ones through would scroll the document during the pinch.
            e.Handled = true;
        }
    }

    /// <summary>Walk <paramref name="stops"/> along the ladder from wherever the editor is now.</summary>
    private static void Step(int stops, Func<double> get, Action<double> set)
    {
        double before = get();
        double zoom = before;
        for (int i = Math.Abs(stops); i > 0; i--)
        {
            zoom = stops > 0 ? ZoomScale.In(zoom) : ZoomScale.Out(zoom);
        }

        // Already hard against 50% or 400%: don't write. A host persists its settings file on the change
        // event, and a pinch that can't move anything shouldn't keep rewriting it.
        if (zoom != before)
        {
            set(zoom);
        }
    }
}
