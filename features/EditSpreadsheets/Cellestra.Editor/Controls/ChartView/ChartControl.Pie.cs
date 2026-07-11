using Avalonia;
using Avalonia.Media;
using Cellestra.Core.Charts;

namespace Cellestra.Desktop.Controls.ChartView;

/// <summary>
/// Pie and doughnut charts: the first series' positive values become wedges drawn clockwise from
/// 12 o'clock, colour-keyed by slice index. The doughnut variant carves an inner radius.
/// </summary>
public sealed partial class ChartControl
{
    private void DrawPie(DrawingContext context, ResolvedChart chart, Rect plot, bool doughnut)
    {
        var series = chart.Series.Count > 0 ? chart.Series[0] : null;
        if (series is null)
        {
            return;
        }

        var slices = new List<(double Value, int Index)>();
        for (int i = 0; i < series.Values.Count; i++)
        {
            double v = series.Values[i];
            if (!double.IsNaN(v) && v > 0)
            {
                slices.Add((v, i));
            }
        }

        double total = slices.Sum(s => s.Value);
        if (total <= 0)
        {
            DrawCentered(context, "Pie needs positive values.", plot, MutedBrush, 12);
            return;
        }

        double radius = Math.Min(plot.Width, plot.Height) / 2 - 12;
        var center = new Point(plot.X + plot.Width / 2, plot.Y + plot.Height / 2);
        double innerRadius = doughnut ? radius * 0.55 : 0;

        double angle = -Math.PI / 2; // start at 12 o'clock
        foreach (var (value, index) in slices)
        {
            double sweep = value / total * Math.PI * 2;
            DrawWedge(context, center, radius, innerRadius, angle, angle + sweep, SeriesBrush(index));
            angle += sweep;
        }
    }

    private static void DrawWedge(DrawingContext context, Point center, double radius, double innerRadius, double start, double end, IBrush brush)
    {
        var geometry = new StreamGeometry();
        bool largeArc = end - start > Math.PI;
        Point Outer(double a) => new(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
        Point Inner(double a) => new(center.X + innerRadius * Math.Cos(a), center.Y + innerRadius * Math.Sin(a));

        using (var ctx = geometry.Open())
        {
            if (innerRadius <= 0)
            {
                ctx.BeginFigure(center, isFilled: true);
                ctx.LineTo(Outer(start));
                ctx.ArcTo(Outer(end), new Size(radius, radius), 0, largeArc, SweepDirection.Clockwise);
                ctx.LineTo(center);
            }
            else
            {
                ctx.BeginFigure(Inner(start), isFilled: true);
                ctx.LineTo(Outer(start));
                ctx.ArcTo(Outer(end), new Size(radius, radius), 0, largeArc, SweepDirection.Clockwise);
                ctx.LineTo(Inner(end));
                ctx.ArcTo(Inner(start), new Size(innerRadius, innerRadius), 0, largeArc, SweepDirection.CounterClockwise);
            }

            ctx.EndFigure(isClosed: true);
        }

        context.DrawGeometry(brush, new Pen(Brushes.White, 1), geometry);
    }
}
