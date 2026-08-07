using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Puffinote.Desktop.Sidebar;

namespace Puffinote.Desktop.Views;

public partial class MainWindow
{
    /// <summary>Accumulates trackpad wheel deltas into whole left/right swipes (see <see cref="SidebarSwipe"/>).</summary>
    private readonly SidebarSwipe _swipe = new();

    /// <summary>
    /// Listen for two-finger horizontal swipes on the touchpad and use them to show/hide the sidebar.
    ///
    /// <para>This handler is deliberately a plain <em>bubbling</em> one, not the tunnelling kind the reserved
    /// keys use: it therefore never sees an event that something on the way up already scrolled with. A
    /// sheet grid being panned sideways, or a document scrolling, handles its own wheel event and this is
    /// simply not called — the gesture can only be claimed by chrome that nobody else wanted.</para>
    /// </summary>
    private void InstallSwipeGestures() =>
        AddHandler(PointerWheelChangedEvent, OnPointerWheelSwipe, RoutingStrategies.Bubble);

    private void OnPointerWheelSwipe(object? sender, PointerWheelEventArgs e)
    {
        // Shift+wheel is how a mouse user asks for a horizontal scroll — an explicit scroll, not a swipe.
        if (e.KeyModifiers != KeyModifiers.None || IsOverHorizontallyScrollable(e.Source as Visual))
        {
            return;
        }

        switch (_swipe.Feed(e.Delta.X, e.Delta.Y, e.Timestamp))
        {
            case SwipeAction.SwipeLeft:
                SetSidebarCollapsed(false);
                break;
            case SwipeAction.SwipeRight:
                SetSidebarCollapsed(true);
                break;
            case SwipeAction.Absorb:
                break; // the rest of a swipe we already acted on — swallow it, but don't act twice
            default:
                return; // not (yet) a swipe: leave the event to whoever else wants it
        }

        e.Handled = true; // only once the gesture is actually ours
    }

    /// <summary>
    /// Whether the wheel event landed inside something that genuinely scrolls sideways — a tab strip with
    /// more tabs than fit, a wide table or code block, a document with lines past the right edge. Those own
    /// the horizontal axis; stealing it for the sidebar would make them unscrollable at their extremes.
    /// </summary>
    private static bool IsOverHorizontallyScrollable(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            // Sub-pixel slack: a viewport and its extent routinely differ by rounding without anything to scroll.
            if (visual is IScrollable scrollable && scrollable.Extent.Width - scrollable.Viewport.Width > 0.5)
            {
                return true;
            }
        }

        return false;
    }
}
