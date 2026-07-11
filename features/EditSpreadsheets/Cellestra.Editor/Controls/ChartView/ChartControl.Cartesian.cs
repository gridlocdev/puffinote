using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Cellestra.Charts;
using Cellestra.Core.Charts;

namespace Cellestra.Desktop.Controls.ChartView;

/// <summary>
/// The Cartesian chart families — column, bar, line, and area — including grouped/stacked/100%
/// bars, line and filled-area series, the value axis with gridlines, category labels, and axis
/// titles. Combo charts route here for their bar/line/area members.
/// </summary>
public sealed partial class ChartControl
{
    private void DrawCartesian(DrawingContext context, ResolvedChart chart, Rect plot)
    {
        int categoryCount = chart.Series.Max(s => s.Values.Count);
        if (categoryCount == 0)
        {
            return;
        }

        bool horizontal = chart.Type == ChartType.Bar;
        bool stacked = chart.Stacking is ChartStacking.Stacked or ChartStacking.Percent;
        bool percent = chart.Stacking == ChartStacking.Percent;

        var (min, max) = ValueExtent(chart, categoryCount, stacked, percent);
        if (Math.Abs(max - min) < 1e-9)
        {
            max = min + 1;
        }

        // Axis gutters: room for value labels and category labels.
        double gutterValue = 44;
        double gutterCategory = 26;
        Rect area = horizontal
            ? new Rect(plot.X + gutterValue, plot.Y + 6, plot.Width - gutterValue - 12, plot.Height - gutterCategory)
            : new Rect(plot.X + gutterValue, plot.Y + 6, plot.Width - gutterValue - 12, plot.Height - gutterCategory);

        // Gridlines + value ticks (along the value axis).
        DrawValueAxis(context, area, min, max, percent, horizontal);

        // Category positions.
        double bandSize = (horizontal ? area.Height : area.Width) / categoryCount;

        // Bar/column families that aren't line/area draw rectangles; line/area draw paths.
        var barSeries = chart.Series.Where(s => s.Type is ChartType.Column or ChartType.Bar).ToList();
        var lineSeries = chart.Series.Where(s => s.Type is ChartType.Line).ToList();
        var areaSeries = chart.Series.Where(s => s.Type is ChartType.Area).ToList();

        // Areas first (filled, behind), then bars, then lines on top.
        foreach (var (series, i) in areaSeries.Select((s, i) => (s, IndexOf(chart, s))))
        {
            DrawAreaSeries(context, series, i, area, categoryCount, min, max, bandSize, horizontal);
        }

        DrawBars(context, chart, barSeries, area, categoryCount, min, max, stacked, percent, horizontal, bandSize);

        foreach (var series in lineSeries)
        {
            DrawLineSeries(context, series, IndexOf(chart, series), area, categoryCount, min, max, bandSize, horizontal);
        }

        // Category labels.
        for (int c = 0; c < categoryCount; c++)
        {
            string label = c < chart.Categories.Count ? chart.Categories[c] : (c + 1).ToString(CultureInfo.InvariantCulture);
            if (label.Length == 0)
            {
                continue;
            }

            var ft = Text(label, 11, MutedBrush);
            if (horizontal)
            {
                double y = area.Y + bandSize * (c + 0.5) - ft.Height / 2;
                context.DrawText(ft, new Point(area.X - ft.Width - 6, y));
            }
            else
            {
                double x = area.X + bandSize * (c + 0.5) - ft.Width / 2;
                context.DrawText(ft, new Point(x, area.Bottom + 6));
            }
        }

        DrawAxisTitles(context, chart, plot, area);
    }

    private static void DrawBars(DrawingContext context, ResolvedChart chart, List<ResolvedSeries> barSeries, Rect area, int categoryCount, double min, double max, bool stacked, bool percent, bool horizontal, double bandSize)
    {
        if (barSeries.Count == 0)
        {
            return;
        }

        double zero = ValuePos(0, min, max, area, horizontal);

        for (int c = 0; c < categoryCount; c++)
        {
            double bandStart = (horizontal ? area.Y : area.X) + bandSize * c;
            double inner = bandSize * 0.7;
            double pad = (bandSize - inner) / 2;

            if (stacked)
            {
                double total = percent ? PositiveSum(barSeries, c) : 1;
                double cursorPos = 0; // running stacked value
                foreach (var (series, si) in barSeries.Select(s => (s, IndexOf(chart, s))))
                {
                    double v = ValueAt(series, c);
                    if (double.IsNaN(v) || v <= 0)
                    {
                        continue;
                    }

                    double display = percent ? (total > 0 ? v / total : 0) : v;
                    double startVal = percent ? (total > 0 ? cursorPos / total : 0) : cursorPos;
                    double p0 = ValuePos(startVal, min, max, area, horizontal);
                    double p1 = ValuePos(startVal + display, min, max, area, horizontal);
                    DrawBarRect(context, SeriesBrush(si), bandStart + pad, inner, p0, p1, horizontal);
                    cursorPos += v;
                }
            }
            else
            {
                double slot = inner / barSeries.Count;
                for (int s = 0; s < barSeries.Count; s++)
                {
                    double v = ValueAt(barSeries[s], c);
                    if (double.IsNaN(v))
                    {
                        continue;
                    }

                    double p1 = ValuePos(v, min, max, area, horizontal);
                    double off = bandStart + pad + slot * s;
                    DrawBarRect(context, SeriesBrush(IndexOf(chart, barSeries[s])), off, slot * 0.9, zero, p1, horizontal);
                }
            }
        }
    }

    private static void DrawBarRect(DrawingContext context, IBrush brush, double bandOffset, double thickness, double basePos, double valuePos, bool horizontal)
    {
        Rect rect = horizontal
            ? new Rect(Math.Min(basePos, valuePos), bandOffset, Math.Abs(valuePos - basePos), thickness)
            : new Rect(bandOffset, Math.Min(basePos, valuePos), thickness, Math.Abs(valuePos - basePos));
        context.FillRectangle(brush, rect);
    }

    private static void DrawLineSeries(DrawingContext context, ResolvedSeries series, int seriesIndex, Rect area, int categoryCount, double min, double max, double bandSize, bool horizontal)
    {
        var pen = new Pen(SeriesBrush(seriesIndex), 2) { LineJoin = PenLineJoin.Round };
        Point? prev = null;
        for (int c = 0; c < categoryCount; c++)
        {
            double v = ValueAt(series, c);
            if (double.IsNaN(v))
            {
                prev = null;
                continue;
            }

            var point = CategoryPoint(c, v, area, min, max, bandSize, horizontal);
            if (prev is { } p)
            {
                context.DrawLine(pen, p, point);
            }

            context.DrawEllipse(SeriesBrush(seriesIndex), null, point, 3, 3);
            prev = point;
        }
    }

    private static void DrawAreaSeries(DrawingContext context, ResolvedSeries series, int seriesIndex, Rect area, int categoryCount, double min, double max, double bandSize, bool horizontal)
    {
        var fill = new SolidColorBrush(((SolidColorBrush)SeriesBrush(seriesIndex)).Color, 0.35);
        var pen = new Pen(SeriesBrush(seriesIndex), 2);
        double zero = ValuePos(0, min, max, area, horizontal);

        var geometry = new StreamGeometry();
        var points = new List<Point>();
        for (int c = 0; c < categoryCount; c++)
        {
            double v = ValueAt(series, c);
            points.Add(CategoryPoint(c, double.IsNaN(v) ? 0 : v, area, min, max, bandSize, horizontal));
        }

        if (points.Count == 0)
        {
            return;
        }

        using (var ctx = geometry.Open())
        {
            var first = horizontal ? new Point(zero, points[0].Y) : new Point(points[0].X, zero);
            ctx.BeginFigure(first, isFilled: true);
            foreach (var p in points)
            {
                ctx.LineTo(p);
            }

            var last = horizontal ? new Point(zero, points[^1].Y) : new Point(points[^1].X, zero);
            ctx.LineTo(last);
            ctx.EndFigure(isClosed: true);
        }

        context.DrawGeometry(fill, null, geometry);

        // Outline along the top of the area.
        for (int i = 1; i < points.Count; i++)
        {
            context.DrawLine(pen, points[i - 1], points[i]);
        }
    }

    private static Point CategoryPoint(int category, double value, Rect area, double min, double max, double bandSize, bool horizontal)
    {
        double along = bandSize * (category + 0.5);
        double across = ValuePos(value, min, max, area, horizontal);
        return horizontal
            ? new Point(across, area.Y + along)
            : new Point(area.X + along, across);
    }

    private void DrawValueAxis(DrawingContext context, Rect area, double min, double max, bool percent, bool horizontal)
    {
        context.DrawRectangle(null, AxisPen, area);
        const int ticks = 5;
        for (int t = 0; t <= ticks; t++)
        {
            double value = min + (max - min) * t / ticks;
            double pos = ValuePos(value, min, max, area, horizontal);
            string label = percent ? (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : FormatNumber(value);
            var ft = Text(label, 10, MutedBrush);
            if (horizontal)
            {
                context.DrawLine(GridPen, new Point(pos, area.Y), new Point(pos, area.Bottom));
                context.DrawText(ft, new Point(pos - ft.Width / 2, area.Bottom + 4));
            }
            else
            {
                context.DrawLine(GridPen, new Point(area.X, pos), new Point(area.Right, pos));
                context.DrawText(ft, new Point(area.X - ft.Width - 6, pos - ft.Height / 2));
            }
        }
    }

    private void DrawAxisTitles(DrawingContext context, ResolvedChart chart, Rect plot, Rect area)
    {
        if (!string.IsNullOrWhiteSpace(chart.XAxisTitle))
        {
            var ft = Text(chart.XAxisTitle!, 11, TextBrush);
            context.DrawText(ft, new Point(area.X + (area.Width - ft.Width) / 2, plot.Bottom - ft.Height));
        }

        if (!string.IsNullOrWhiteSpace(chart.YAxisTitle))
        {
            var ft = Text(chart.YAxisTitle!, 11, TextBrush);
            // Drawn at the top-left of the value axis (kept simple — no rotation).
            context.DrawText(ft, new Point(plot.X + 2, plot.Y));
        }
    }
}
