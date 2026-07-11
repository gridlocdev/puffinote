using Avalonia;
using Avalonia.Media;
using Cellestra.Core.Charts;

namespace Cellestra.Desktop.Controls.ChartView;

/// <summary>
/// Scatter and bubble charts: each series is plotted against a numeric X axis (its own X values, or
/// 1-based index when absent) and a shared Y value axis. Bubble points scale their radius by the
/// series' bubble-size channel.
/// </summary>
public sealed partial class ChartControl
{
    private void DrawScatter(DrawingContext context, ResolvedChart chart, Rect plot, bool bubble)
    {
        Rect area = new(plot.X + 44, plot.Y + 6, plot.Width - 56, plot.Height - 26);
        double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var s in chart.Series)
        {
            for (int i = 0; i < s.Values.Count; i++)
            {
                double y = s.Values[i];
                double x = s.XValues is { } xs && i < xs.Count ? xs[i] : i + 1;
                if (double.IsNaN(x) || double.IsNaN(y))
                {
                    continue;
                }

                xMin = Math.Min(xMin, x);
                xMax = Math.Max(xMax, x);
                yMin = Math.Min(yMin, y);
                yMax = Math.Max(yMax, y);
            }
        }

        if (xMin > xMax)
        {
            return;
        }

        yMin = Math.Min(yMin, 0);
        if (Math.Abs(xMax - xMin) < 1e-9)
        {
            xMax = xMin + 1;
        }
        if (Math.Abs(yMax - yMin) < 1e-9)
        {
            yMax = yMin + 1;
        }

        context.DrawRectangle(null, AxisPen, area);
        DrawValueAxis(context, area, yMin, yMax, percent: false, horizontal: false);

        double maxSize = 1;
        if (bubble)
        {
            maxSize = chart.Series.SelectMany(s => s.BubbleSizes ?? [])
                .Where(v => !double.IsNaN(v) && v > 0).DefaultIfEmpty(1).Max();
        }

        foreach (var (series, si) in chart.Series.Select((s, i) => (s, i)))
        {
            for (int i = 0; i < series.Values.Count; i++)
            {
                double y = series.Values[i];
                double x = series.XValues is { } xs && i < xs.Count ? xs[i] : i + 1;
                if (double.IsNaN(x) || double.IsNaN(y))
                {
                    continue;
                }

                double px = area.X + (x - xMin) / (xMax - xMin) * area.Width;
                double py = area.Bottom - (y - yMin) / (yMax - yMin) * area.Height;
                double r = 4;
                if (bubble && series.BubbleSizes is { } sizes && i < sizes.Count && !double.IsNaN(sizes[i]) && sizes[i] > 0)
                {
                    r = 4 + 18 * Math.Sqrt(sizes[i] / maxSize);
                }

                var brush = new SolidColorBrush(((SolidColorBrush)SeriesBrush(si)).Color, bubble ? 0.55 : 1.0);
                context.DrawEllipse(brush, null, new Point(px, py), r, r);
            }
        }

        DrawAxisTitles(context, chart, plot, area);
    }
}
