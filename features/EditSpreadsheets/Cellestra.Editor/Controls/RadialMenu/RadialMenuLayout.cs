using System;

namespace Cellestra.Desktop.Controls.RadialMenu;

/// <summary>
/// Pure geometry for the press-and-hold radial menu, kept UI-free so it can be unit-tested without a
/// window (AGENTS.md: pointer-driven UI is hard to drive headlessly — put the logic in a helper).
/// Segments are numbered clockwise from the top (segment 0 points straight up). The direction the
/// cursor is dragged from the centre picks the segment; staying within the dead zone picks none.
/// </summary>
public static class RadialMenuLayout
{
    /// <summary>
    /// The segment the cursor points at, given its offset (<paramref name="dx"/>,
    /// <paramref name="dy"/>) from the radial's centre in screen pixels (x right, y down). Returns
    /// -1 when within <paramref name="deadZoneRadius"/> of the centre (no selection / a cancel),
    /// or when there are no segments. Segment 0 is centred on straight-up; higher indices proceed
    /// clockwise.
    /// </summary>
    public static int SegmentAt(double dx, double dy, int segmentCount, double deadZoneRadius)
    {
        if (segmentCount <= 0)
        {
            return -1;
        }

        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance < deadZoneRadius)
        {
            return -1;
        }

        // Clockwise angle from straight up: up is -y, right is +x, so atan2(dx, -dy) is 0 at the top
        // and increases clockwise.
        double angle = Math.Atan2(dx, -dy);
        if (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        double step = 2 * Math.PI / segmentCount;
        int segment = (int)Math.Round(angle / step) % segmentCount;
        return segment;
    }

    /// <summary>The clockwise-from-top centre angle (radians) of segment <paramref name="index"/>.</summary>
    public static double SegmentCenterAngle(int index, int segmentCount) =>
        segmentCount <= 0 ? 0 : index * (2 * Math.PI / segmentCount);

    /// <summary>
    /// A point on the radial at clockwise-from-top <paramref name="angle"/> (radians) and
    /// <paramref name="radius"/>, relative to a centre at (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>). Mirrors <see cref="SegmentAt"/>'s convention (x = sin, y = -cos).
    /// </summary>
    public static (double X, double Y) Polar(double centerX, double centerY, double radius, double angle) =>
        (centerX + (radius * Math.Sin(angle)), centerY - (radius * Math.Cos(angle)));
}
