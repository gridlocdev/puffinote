using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Pivotask.Control.Views;

/// <summary>
/// Holds a surface's repaint back until the click that provoked it has finished being delivered.
///
/// <para>The board and the list rebuild wholesale on every document change, which is what keeps them from
/// quietly disagreeing with the model. The cost is that a rebuild replaces every card and column with new
/// objects — and a click is only a click if the press and the release land on the <em>same</em> object. So
/// a change that happens between the two, which is exactly what committing a half-typed field on focus
/// loss does, deletes the control the click was on its way to: the card never opens, the button never
/// fires, and the click appears to have been eaten.</para>
///
/// <para>Deferring by a dispatcher turn does not fix it. The dispatcher runs between the press and the
/// release — in the headless harness and in a real window alike — so the rebuild simply lands there
/// instead. What has to be waited on is the gesture, not a turn.</para>
/// </summary>
internal sealed class GestureGate
{
    private readonly Action _rebuild;
    private bool _pressed;
    private bool _pending;

    public GestureGate(InputElement surface, Action rebuild)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _rebuild = rebuild;

        // Tunnel down, so the press is noted before anything reacts to it; bubble back up for the end,
        // after every handler that cares has run. handledEventsToo throughout — a handled press is still
        // a gesture in flight.
        surface.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        surface.AddHandler(InputElement.PointerReleasedEvent, OnEnded, RoutingStrategies.Bubble, handledEventsToo: true);
        surface.AddHandler(InputElement.PointerCaptureLostEvent, OnEnded, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Whether the caller should skip repainting for now. Records that one is owed, so the gesture's end
    /// can honour it.
    /// </summary>
    public bool Defer()
    {
        if (!_pressed)
        {
            return false;
        }

        _pending = true;
        return true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e) => _pressed = true;

    private void OnEnded(object? sender, RoutedEventArgs e)
    {
        _pressed = false;
        if (!_pending)
        {
            return;
        }

        _pending = false;

        // Posted rather than immediate: the release is still being routed, and Tapped — the gesture the
        // card opens on — is raised further along that same routing.
        Dispatcher.UIThread.Post(_rebuild);
    }
}
