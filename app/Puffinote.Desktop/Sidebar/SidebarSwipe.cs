namespace Puffinote.Desktop.Sidebar;

/// <summary>What a wheel event fed to <see cref="SidebarSwipe"/> amounted to.</summary>
internal enum SwipeAction
{
    /// <summary>Not part of a swipe — leave the event alone so it can scroll whatever it was aimed at.</summary>
    None,

    /// <summary>The tail of a swipe already acted on — swallow it, but do nothing (one swipe, one action).</summary>
    Absorb,

    /// <summary>Two fingers travelled left far enough to count as a swipe.</summary>
    SwipeLeft,

    /// <summary>Two fingers travelled right far enough to count as a swipe.</summary>
    SwipeRight,
}

/// <summary>
/// Turns the stream of wheel deltas a touchpad produces into at most one left/right swipe per physical
/// gesture (PRD §7.2.1 — the sidebar collapses by pointer as well as by key).
///
/// <para>A trackpad two-finger swipe arrives as dozens of small <c>PointerWheelChanged</c> events rather
/// than one gesture, so this accumulates them: an event only counts as horizontal when its X delta clearly
/// dominates its Y delta (<see cref="DominanceRatio"/>) — otherwise it's a vertical or diagonal scroll and
/// whatever was accumulating is abandoned — and a swipe only fires once the accumulated travel passes
/// <see cref="Threshold"/>. After firing, the rest of the gesture (including the trackpad's momentum tail)
/// is absorbed rather than re-fired; a gap of <see cref="IdleResetMs"/> with no horizontal event ends the
/// gesture and re-arms. That gap is also what separates two deliberate swipes, so the accumulator never
/// carries stray delta from minutes ago into the next one.</para>
///
/// <para>Sign convention matches the rest of the repo (<c>SpreadsheetGrid.ApplyWheelScroll</c>): a positive
/// X delta means "toward the left edge", which on a natural-scrolling trackpad is fingers moving <em>right</em>.
/// Hence positive travel is <see cref="SwipeAction.SwipeRight"/>.</para>
/// </summary>
internal sealed class SidebarSwipe
{
    /// <summary>
    /// Accumulated horizontal travel, in wheel notches, that makes a swipe. The repo scrolls 40px per notch
    /// (<c>SpreadsheetGrid.WheelPixelsPerNotch</c>), so this is ~120px of finger travel — far enough that a
    /// stray flick while scrolling a document can't reach it, short enough to feel immediate.
    /// </summary>
    public const double Threshold = 3.0;

    /// <summary>How much the X delta must beat the Y delta by for an event to count as horizontal at all.</summary>
    public const double DominanceRatio = 1.5;

    /// <summary>A gap this long (ms) with no horizontal event ends the gesture: reset the travel, re-arm.</summary>
    public const ulong IdleResetMs = 250;

    private double _travel;
    private ulong _lastMs;
    private bool _spent;

    /// <summary>
    /// Feed one wheel event. <paramref name="timestampMs"/> is the event's own timestamp (not a clock read),
    /// so the idle gap is measured in input time and the whole thing stays deterministic under test.
    /// </summary>
    public SwipeAction Feed(double deltaX, double deltaY, ulong timestampMs)
    {
        // A backwards timestamp shouldn't underflow into a 500-million-year gap; treat it as a fresh gesture.
        bool idle = timestampMs < _lastMs || timestampMs - _lastMs >= IdleResetMs;
        _lastMs = timestampMs;
        if (idle)
        {
            _travel = 0;
            _spent = false;
        }

        // Vertical or diagonal: not a swipe. Abandon the accumulation — a swipe is a horizontal gesture, and
        // half of one followed by a scroll down shouldn't complete later.
        if (Math.Abs(deltaX) <= Math.Abs(deltaY) * DominanceRatio)
        {
            _travel = 0;
            return SwipeAction.None;
        }

        if (_spent)
        {
            return SwipeAction.Absorb; // same physical swipe, already acted on
        }

        // A reversal is a new swipe, not a cancellation of the old one — start counting again from here.
        if (_travel != 0 && Math.Sign(_travel) != Math.Sign(deltaX))
        {
            _travel = 0;
        }

        _travel += deltaX;
        if (Math.Abs(_travel) < Threshold)
        {
            return SwipeAction.None; // still gathering — the event is nobody's yet
        }

        var action = _travel > 0 ? SwipeAction.SwipeRight : SwipeAction.SwipeLeft;
        _travel = 0;
        _spent = true;
        return action;
    }
}
