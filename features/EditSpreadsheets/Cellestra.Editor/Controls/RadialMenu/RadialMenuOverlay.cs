using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Cellestra.Desktop.Controls.Icons;

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
    private bool _showLabels = true;
    private int _activeSegment = -1;

    public RadialMenuOverlay()
    {
        // Always part of the layout (so it is measured/arranged); it simply draws nothing until opened.
        IsHitTestVisible = false;
    }

    /// <summary>The radial's dead-zone radius — a cursor closer than this to the centre selects nothing.</summary>
    public static double DeadZoneRadius => InnerRadius;

    /// <summary>Open the radial centred at <paramref name="center"/> with the given segments.</summary>
    public void Open(Point center, IReadOnlyList<RadialSegment> segments, bool showLabels)
    {
        _center = center;
        _segments = segments;
        _showLabels = showLabels;
        _activeSegment = -1;
        InvalidateVisual();
    }

    /// <summary>Highlight the segment the cursor now points at (-1 for none); repaints if it changed.</summary>
    public void SetActiveSegment(int segment)
    {
        if (segment == _activeSegment)
        {
            return;
        }

        _activeSegment = segment;
        InvalidateVisual();
    }

    public void Hide()
    {
        _segments = Array.Empty<RadialSegment>();
        _activeSegment = -1;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        int count = _segments.Count;
        if (count == 0)
        {
            return;
        }

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
        context.DrawEllipse(InnerFill, RimPen, _center, InnerRadius, InnerRadius);

        for (int i = 0; i < count; i++)
        {
            DrawSegmentContent(context, i, count);
        }
    }

    private Geometry BuildWedge(double fromAngle, double toAngle)
    {
        var (ix1, iy1) = RadialMenuLayout.Polar(_center.X, _center.Y, InnerRadius, fromAngle);
        var (ox1, oy1) = RadialMenuLayout.Polar(_center.X, _center.Y, OuterRadius, fromAngle);
        var (ox2, oy2) = RadialMenuLayout.Polar(_center.X, _center.Y, OuterRadius, toAngle);
        var (ix2, iy2) = RadialMenuLayout.Polar(_center.X, _center.Y, InnerRadius, toAngle);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(ix1, iy1), isFilled: true);
            ctx.LineTo(new Point(ox1, oy1));
            ctx.ArcTo(new Point(ox2, oy2), new Size(OuterRadius, OuterRadius), 0, isLargeArc: false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(ix2, iy2));
            ctx.ArcTo(new Point(ix1, iy1), new Size(InnerRadius, InnerRadius), 0, isLargeArc: false, SweepDirection.CounterClockwise);
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
        double contentRadius = (InnerRadius + OuterRadius) / 2;

        // With labels on, lift the icon a touch so the label sits beneath it within the wedge.
        double iconRadius = _showLabels ? contentRadius - 8 : contentRadius;
        var (iconX, iconY) = RadialMenuLayout.Polar(_center.X, _center.Y, iconRadius, angle);
        DrawIcon(context, segment.IconKey, iconX, iconY, brush);

        if (_showLabels && !string.IsNullOrEmpty(segment.Label))
        {
            var (labelX, labelY) = RadialMenuLayout.Polar(_center.X, _center.Y, contentRadius + 16, angle);
            DrawLabel(context, segment.Label, labelX, labelY, brush);
        }
    }

    private static void DrawIcon(DrawingContext context, string? iconKey, double centerX, double centerY, IBrush brush)
    {
        var geometry = LucideIcons.Get(iconKey);
        if (geometry is null)
        {
            return;
        }

        double scale = IconSize / LucideIcons.ViewBox;
        var pen = new Pen(brush, 2) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var transform = Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(centerX - (IconSize / 2), centerY - (IconSize / 2));
        using (context.PushTransform(transform))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private static void DrawLabel(DrawingContext context, string label, double centerX, double centerY, IBrush brush)
    {
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 10.5, brush)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = (OuterRadius - InnerRadius) + 24,
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1,
        };

        context.DrawText(text, new Point(centerX - (text.Width / 2), centerY - (text.Height / 2)));
    }
}
