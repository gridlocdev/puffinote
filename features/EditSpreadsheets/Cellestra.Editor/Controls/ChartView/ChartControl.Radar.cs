using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Cellestra.Core.Charts;

namespace Cellestra.Desktop.Controls.ChartView;

/// <summary>
/// Radar and filled-radar charts: categories become spokes radiating from the centre, each series a
/// closed polygon whose vertices scale by value against the shared maximum. The filled variant tints
/// the polygon interior.
/// </summary>
public sealed partial class ChartControl
{
    private void DrawRadar(DrawingContext context, ResolvedChart chart, Rect plot, bool filled)
    {
        int axes = chart.Series.Max(s => s.Values.Count);
        if (axes < 3)
        {
            DrawCentered(context, "Radar needs at least 3 categories.", plot, MutedBrush, 12);
            return;
        }

        double max = chart.Series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Max();
        if (max <= 0)
        {
            max = 1;
        }

        double radius = Math.Min(plot.Width, plot.Height) / 2 - 28;
        var center = new Point(plot.X + plot.Width / 2, plot.Y + plot.Height / 2);

        // Spokes + category labels.
        for (int a = 0; a < axes; a++)
        {
            double ang = -Math.PI / 2 + a * Math.PI * 2 / axes;
            var outer = new Point(center.X + radius * Math.Cos(ang), center.Y + radius * Math.Sin(ang));
            context.DrawLine(GridPen, center, outer);
            string label = a < chart.Categories.Count ? chart.Categories[a] : (a + 1).ToString(CultureInfo.InvariantCulture);
            if (label.Length > 0)
            {
                var ft = Text(label, 10, MutedBrush);
                context.DrawText(ft, new Point(outer.X - ft.Width / 2, outer.Y - ft.Height / 2));
            }
        }

        foreach (var (series, si) in chart.Series.Select((s, i) => (s, i)))
        {
            var points = new List<Point>();
            for (int a = 0; a < axes; a++)
            {
                double v = a < series.Values.Count ? series.Values[a] : double.NaN;
                double mag = double.IsNaN(v) ? 0 : Math.Max(0, v) / max;
                double ang = -Math.PI / 2 + a * Math.PI * 2 / axes;
                points.Add(new Point(center.X + radius * mag * Math.Cos(ang), center.Y + radius * mag * Math.Sin(ang)));
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], isFilled: filled);
                for (int i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(points[i]);
                }

                ctx.EndFigure(isClosed: true);
            }

            var stroke = new Pen(SeriesBrush(si), 2) { LineJoin = PenLineJoin.Round };
            IBrush? fill = filled ? new SolidColorBrush(((SolidColorBrush)SeriesBrush(si)).Color, 0.3) : null;
            context.DrawGeometry(fill, stroke, geometry);
        }
    }
}
