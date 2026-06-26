using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.Icons;
using CommandBarControl = Cellestra.Desktop.Controls.CommandBar.CommandBar;

namespace Cellestra.Desktop.Controls.RadialMenu;

/// <summary>One wedge of the radial: the command it runs (by id), its glyph + label, and whether it is enabled right now.</summary>
public readonly record struct RadialSegment(string? CommandId, string? IconKey, string Label, bool Enabled);

/// <summary>
/// The press-and-hold radial command menu, drawn as a ring of wedges centred on the cursor. It is a
/// purely visual overlay (<see cref="IsHitTestVisible"/> is false): the owning grid captures the
/// pointer, feeds it the active segment as the cursor moves, and runs the bound command on release.
/// The look is a translucent dark HUD with light glyphs so it reads on both the light and dark theme
/// without coupling to the grid palette.
/// </summary>
public sealed class RadialMenuOverlay : Control
{
    private const double OuterRadius = 104;
    private const double InnerRadius = 40;
    private const double IconSize = 22;

    private static readonly IBrush GlassFill = new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0x24, 0x26, 0x2B));
    private static readonly IBrush ActiveFill = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x7D, 0xE1));
    private static readonly IBrush ActiveDisabledFill = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0x4A, 0x4F, 0x57));
    private static readonly IBrush InnerFill = new ImmutableSolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x1A, 0x1E));
    private static readonly IBrush GlyphBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF7));
    private static readonly IBrush DisabledGlyphBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0x82, 0x86, 0x8E));
    private static readonly ImmutablePen Divider = new(new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly ImmutablePen RimPen = new(new ImmutableSolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0x00)), 1);

    private Point _center;
    private IReadOnlyList<RadialSegment> _segments = Array.Empty<RadialSegment>();
    private int _activeSegment = -1;
    private Point _drawCenter;
    private double _drawScale = 1;

    public RadialMenuOverlay()
    {
        // Always part of the layout (so it is measured/arranged); it simply draws nothing until opened.
        IsHitTestVisible = false;
    }

    /// <summary>
    /// When true the radial is centred on the control's own bounds and scaled to fit them. Used by the
    /// settings preview, where the overlay sits in a fixed box; the grid leaves this false so the radial
    /// appears full-size centred on the cursor.
    /// </summary>
    public bool FitToBounds { get; set; }

    /// <summary>The radial's dead-zone radius — a cursor closer than this to the centre selects nothing.</summary>
    public static double DeadZoneRadius => InnerRadius;

    /// <summary>
    /// The segment a point in the control's own coordinate space falls in (-1 for the dead zone / none),
    /// honouring the current centre and fit-to-bounds scale. Lets a hit-test-invisible preview map
    /// pointer events (forwarded from its parent) back to a segment.
    /// </summary>
    public int SegmentAtLocalPoint(Point p)
    {
        var (center, scale) = ComputeLayout();
        return RadialMenuLayout.SegmentAt(p.X - center.X, p.Y - center.Y, _segments.Count, InnerRadius * scale);
    }

    /// <summary>The centre and uniform scale to draw at — fitted to bounds, or the opened centre at 1:1.</summary>
    private (Point Center, double Scale) ComputeLayout()
    {
        if (!FitToBounds)
        {
            return (_center, 1);
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double natural = OuterRadius + 4;
        double available = (Math.Min(Bounds.Width, Bounds.Height) / 2) - 2;
        double scale = available > 0 && natural > 0 ? available / natural : 1;
        return (center, scale);
    }

    /// <summary>Open the radial centred at <paramref name="center"/> with the given segments.</summary>
    public void Open(Point center, IReadOnlyList<RadialSegment> segments)
    {
        _center = center;
        _segments = segments;
        _activeSegment = -1;
        UpdateTooltip();
        InvalidateVisual();
    }

    /// <summary>The currently highlighted segment (-1 for none).</summary>
    public int ActiveSegment => _activeSegment;

    /// <summary>Test seam: the id of the command whose tip the active wedge floats (null when none).</summary>
    internal string? ActiveTooltipCommandIdForTest => ActiveCommand()?.Id;

    /// <summary>Test seam: whether the floating command tooltip is currently open.</summary>
    internal bool TooltipOpenForTest => ToolTip.GetIsOpen(this);

    /// <summary>Highlight the segment the cursor now points at (-1 for none); repaints if it changed.</summary>
    public void SetActiveSegment(int segment)
    {
        if (segment == _activeSegment)
        {
            return;
        }

        _activeSegment = segment;
        UpdateTooltip();
        InvalidateVisual();
    }

    public void Hide()
    {
        _segments = Array.Empty<RadialSegment>();
        _activeSegment = -1;
        UpdateTooltip();
        InvalidateVisual();
    }

    /// <summary>The command the highlighted wedge runs, or null when nothing/an unbound wedge is active.</summary>
    private AppCommand? ActiveCommand()
    {
        if (_activeSegment < 0 || _activeSegment >= _segments.Count)
        {
            return null;
        }

        string? id = _segments[_activeSegment].CommandId;
        return id is null ? null : AppCommands.ById(id);
    }

    /// <summary>
    /// Float a real tooltip over the highlighted wedge — the same name + shortcut + description card the
    /// command bar shows (<see cref="CommandBar.BuildTip"/>) — rather than drawing a label inside the
    /// ring. Reopened on each change so it repositions on the new wedge; closed when nothing is active.
    /// </summary>
    private void UpdateTooltip()
    {
        // Only drive the popup once shown; merely constructing the overlay (e.g. in a test) opens nothing.
        if (TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        ToolTip.SetIsOpen(this, false);
        var command = ActiveCommand();
        if (command is null)
        {
            return;
        }

        ToolTip.SetTip(this, CommandBarControl.BuildTip(command));
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 0);
        ToolTip.SetIsOpen(this, true);
    }

    public override void Render(DrawingContext context)
    {
        int count = _segments.Count;
        if (count == 0)
        {
            return;
        }

        (_drawCenter, _drawScale) = ComputeLayout();

        double step = 2 * Math.PI / count;
        for (int i = 0; i < count; i++)
        {
            double centerAngle = RadialMenuLayout.SegmentCenterAngle(i, count);
            double from = centerAngle - (step / 2);
            double to = centerAngle + (step / 2);

            var segment = _segments[i];
            IBrush fill = i == _activeSegment
                ? (segment.Enabled ? ActiveFill : ActiveDisabledFill)
                : GlassFill;

            var wedge = BuildWedge(from, to);
            context.DrawGeometry(fill, Divider, wedge);
        }

        // The hollow centre is a clear "release here to cancel" target.
        double innerR = InnerRadius * _drawScale;
        context.DrawEllipse(InnerFill, RimPen, _drawCenter, innerR, innerR);

        for (int i = 0; i < count; i++)
        {
            DrawSegmentContent(context, i, count);
        }

        // The active wedge's command title floats as a real tooltip popup (see UpdateTooltip), not drawn
        // into the ring — so it matches the command bar's hover card and can spill outside the radial.
    }

    private Geometry BuildWedge(double fromAngle, double toAngle)
    {
        double innerR = InnerRadius * _drawScale;
        double outerR = OuterRadius * _drawScale;
        var (ix1, iy1) = RadialMenuLayout.Polar(_drawCenter.X, _drawCenter.Y, innerR, fromAngle);
        var (ox1, oy1) = RadialMenuLayout.Polar(_drawCenter.X, _drawCenter.Y, outerR, fromAngle);
        var (ox2, oy2) = RadialMenuLayout.Polar(_drawCenter.X, _drawCenter.Y, outerR, toAngle);
        var (ix2, iy2) = RadialMenuLayout.Polar(_drawCenter.X, _drawCenter.Y, innerR, toAngle);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(ix1, iy1), isFilled: true);
            ctx.LineTo(new Point(ox1, oy1));
            ctx.ArcTo(new Point(ox2, oy2), new Size(outerR, outerR), 0, isLargeArc: false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(ix2, iy2));
            ctx.ArcTo(new Point(ix1, iy1), new Size(innerR, innerR), 0, isLargeArc: false, SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private void DrawSegmentContent(DrawingContext context, int index, int count)
    {
        var segment = _segments[index];
        IBrush brush = segment.Enabled ? GlyphBrush : DisabledGlyphBrush;
        if (index == _activeSegment && segment.Enabled)
        {
            brush = GlyphBrush; // stays light on the accent fill
        }

        double angle = RadialMenuLayout.SegmentCenterAngle(index, count);
        double contentRadius = (InnerRadius + OuterRadius) / 2 * _drawScale;
        double iconSize = IconSize * _drawScale;

        var (iconX, iconY) = RadialMenuLayout.Polar(_drawCenter.X, _drawCenter.Y, contentRadius, angle);
        DrawIcon(context, segment.IconKey, iconX, iconY, brush, iconSize);
    }

    private static void DrawIcon(DrawingContext context, string? iconKey, double centerX, double centerY, IBrush brush, double iconSize)
    {
        var geometry = LucideIcons.Get(iconKey);
        if (geometry is null)
        {
            return;
        }

        double scale = iconSize / LucideIcons.ViewBox;
        var pen = new Pen(brush, 2) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var transform = Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(centerX - (iconSize / 2), centerY - (iconSize / 2));
        using (context.PushTransform(transform))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
