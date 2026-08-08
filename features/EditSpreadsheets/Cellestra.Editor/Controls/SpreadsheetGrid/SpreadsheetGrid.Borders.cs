using Avalonia;
using Avalonia.Media;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Draws each cell's <see cref="CellStyle.Border"/> on top of the faint gridlines so a set border
/// reads clearly (the Core model and ODS mapping already exist; this is the render half). Edges map
/// to an Avalonia <see cref="Pen"/> via <see cref="BorderPenStyle"/> — thickness plus dash — scaled
/// by zoom. Stays virtualized (only the visible cells) and respects merges: the anchor's border is
/// drawn around the whole merged rect, never on internal seams.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private void DrawCellBorders(DrawingContext context, Worksheet sheet)
    {
        // Single cells, virtualized to the viewport.
        foreach (var (row, y, h) in _visibleRows)
        {
            foreach (var (col, x, w) in _visibleColumns)
            {
                if (sheet.FindMerge(row, col) is not null)
                {
                    continue; // merged regions are bordered once, below
                }

                var cell = sheet.Find(row, col);
                if (cell is null)
                {
                    continue; // no cell object → default (no) border
                }

                var border = ResolveStyle(row, col).Border;
                if (border.HasAnyEdge)
                {
                    DrawBorderEdges(context, new Rect(x, y, w, h), border);
                }
            }
        }

        // Merged regions: the anchor's border outlines the whole rect, not the internal seams.
        foreach (var region in sheet.MergedRegions)
        {
            var anchor = sheet.Find(region.Range.Top, region.Range.Left);
            if (anchor is null)
            {
                continue;
            }

            var border = ResolveStyle(region.Range.Top, region.Range.Left).Border;
            if (border.HasAnyEdge &&
                TryGetRegionRect(region.Range.Top, region.Range.Left, region.Range.Bottom, region.Range.Right, out var rect))
            {
                DrawBorderEdges(context, rect, border);
            }
        }
    }

    private void DrawBorderEdges(DrawingContext context, Rect rect, Border border)
    {
        // Top-left, top-right, bottom-left, bottom-right corners of the cell rect.
        var tl = rect.TopLeft;
        var tr = rect.TopRight;
        var bl = rect.BottomLeft;
        var br = rect.BottomRight;

        DrawEdge(context, border.Top, tl, tr, horizontal: true);
        DrawEdge(context, border.Bottom, bl, br, horizontal: true);
        DrawEdge(context, border.Left, tl, bl, horizontal: false);
        DrawEdge(context, border.Right, tr, br, horizontal: false);
    }

    private void DrawEdge(DrawingContext context, BorderEdge edge, Point from, Point to, bool horizontal)
    {
        if (!edge.IsVisible)
        {
            return;
        }

        var (thickness, dash) = BorderPenStyle.For(edge.Style, Zoom);
        var brush = new SolidColorBrush(ToColor(edge.Color));

        if (edge.Style == BorderLineStyle.Double)
        {
            // Two parallel hairlines straddling the edge, a small gap apart.
            double gap = Math.Max(1, thickness);
            var pen = new Pen(brush, thickness) { LineCap = PenLineCap.Square };
            (Point a1, Point b1, Point a2, Point b2) = horizontal
                ? (Offset(from, 0, -gap), Offset(to, 0, -gap), Offset(from, 0, gap), Offset(to, 0, gap))
                : (Offset(from, -gap, 0), Offset(to, -gap, 0), Offset(from, gap, 0), Offset(to, gap, 0));
            context.DrawLine(pen, a1, b1);
            context.DrawLine(pen, a2, b2);
            return;
        }

        var single = new Pen(brush, thickness)
        {
            LineCap = PenLineCap.Square,
            DashStyle = dash,
        };
        context.DrawLine(single, from, to);
    }

    private static Point Offset(Point p, double dx, double dy) => new(p.X + dx, p.Y + dy);
}

/// <summary>
/// Maps a <see cref="BorderLineStyle"/> to the on-screen pen thickness (in device pixels, scaled by
/// zoom) and dash pattern. Pure and zoom-aware so it can be unit-tested without a render surface.
/// </summary>
public static class BorderPenStyle
{
    /// <summary>The pen thickness and dash pattern for <paramref name="style"/> at the given <paramref name="zoom"/>.</summary>
    public static (double Thickness, DashStyle? Dash) For(BorderLineStyle style, double zoom)
    {
        // Base point-widths mirror the ODS mapping (OdsCellStyles.BorderToOdf): thin 0.5pt, medium
        // 1pt, thick 2pt. On screen we floor each at 1px so a thin border never vanishes, then scale
        // medium/thick up with zoom so they stay visibly heavier than the faint gridlines.
        double t = style switch
        {
            BorderLineStyle.Medium => 1.5 * zoom,
            BorderLineStyle.Thick => 2.5 * zoom,
            BorderLineStyle.Double => 1.0 * zoom,
            _ => 1.0 * Math.Max(1.0, zoom), // Thin / Dashed / Dotted
        };
        t = Math.Max(1.0, t);

        DashStyle? dash = style switch
        {
            // Dash/dot lengths are in pen-thickness units, so they scale with thickness automatically.
            BorderLineStyle.Dashed => new DashStyle([3, 2], 0),
            BorderLineStyle.Dotted => new DashStyle([1, 2], 0),
            _ => null,
        };

        return (t, dash);
    }
}
