using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Cellestra.Charts;
using Cellestra.Core.Charts;

namespace Cellestra.Desktop.Controls.ChartView;

/// <summary>
/// A from-scratch chart renderer — no third-party charting library. It draws a
/// <see cref="ResolvedChart"/> with Avalonia's <see cref="DrawingContext"/> directly, the same
/// "own the pixels" approach the spreadsheet grid takes. One control covers every type Cellestra
/// supports (column, bar, line, area, pie, doughnut, scatter, bubble, radar, filled radar), with
/// grouped/stacked/100%-stacked variants and per-series type overrides for combo charts.
/// </summary>
public sealed class ChartControl : Control
{
    // A categorical palette with reasonable contrast; series cycle through it.
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x2B, 0x6C, 0xB0), // blue
        Color.FromRgb(0x15, 0x73, 0x4F), // green
        Color.FromRgb(0xC2, 0x5A, 0x1A), // orange
        Color.FromRgb(0x8E, 0x44, 0xAD), // purple
        Color.FromRgb(0xB0, 0x2A, 0x37), // red
        Color.FromRgb(0x1F, 0x9E, 0xA8), // teal
        Color.FromRgb(0xB8, 0x8A, 0x00), // gold
        Color.FromRgb(0x55, 0x6B, 0x2F), // olive
    ];

    private ResolvedChart? _chart;

    /// <summary>Show <paramref name="chart"/> (replacing any previous one) and repaint.</summary>
    public void Show(ResolvedChart? chart)
    {
        _chart = chart;
        InvalidateVisual();
    }

    private bool IsDark => ActualThemeVariant == ThemeVariant.Dark;

    private IBrush TextBrush => new SolidColorBrush(IsDark ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromRgb(0x22, 0x22, 0x22));

    private IBrush MutedBrush => new SolidColorBrush(IsDark ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x6A, 0x6A, 0x6A));

    private IPen AxisPen => new Pen(new SolidColorBrush(IsDark ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(0xBB, 0xBB, 0xBB)), 1);

    private IPen GridPen => new Pen(new SolidColorBrush(IsDark ? Color.FromArgb(0x40, 0xAA, 0xAA, 0xAA) : Color.FromArgb(0x40, 0x88, 0x88, 0x88)), 1);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(IsDark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White), bounds);

        var chart = _chart;
        if (chart is null || chart.IsEmpty)
        {
            DrawCentered(context, chart is null ? "No chart" : "No data — set the ranges on the right.", bounds, MutedBrush, 14);
            return;
        }

        double top = 12;
        if (!string.IsNullOrWhiteSpace(chart.Title))
        {
            var title = Text(chart.Title, 16, TextBrush, bold: true);
            context.DrawText(title, new Point((bounds.Width - title.Width) / 2, top));
            top += title.Height + 8;
        }

        var legend = chart.ShowLegend ? BuildLegend(chart) : null;
        double bottomReserve = legend?.Height ?? 0;

        var plot = new Rect(0, top, bounds.Width, Math.Max(0, bounds.Height - top - bottomReserve - 8));
        if (plot.Width < 40 || plot.Height < 40)
        {
            return; // too small to draw meaningfully
        }

        switch (chart.Type)
        {
            case ChartType.Pie:
            case ChartType.Doughnut:
                DrawPie(context, chart, plot, chart.Type == ChartType.Doughnut);
                break;
            case ChartType.Scatter:
            case ChartType.Bubble:
                DrawScatter(context, chart, plot, chart.Type == ChartType.Bubble);
                break;
            case ChartType.Radar:
            case ChartType.FilledRadar:
                DrawRadar(context, chart, plot, chart.Type == ChartType.FilledRadar);
                break;
            default:
                DrawCartesian(context, chart, plot);
                break;
        }

        if (legend is not null)
        {
            DrawLegend(context, legend, new Rect(0, bounds.Height - bottomReserve, bounds.Width, bottomReserve));
        }
    }

    // ---- Cartesian families (column / bar / line / area) ------------------

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

        DrawAxisTitles(context, chart, plot, area, horizontal);
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

    private void DrawAxisTitles(DrawingContext context, ResolvedChart chart, Rect plot, Rect area, bool horizontal)
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

    // ---- Pie / doughnut ---------------------------------------------------

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

    // ---- Scatter / bubble -------------------------------------------------

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

        DrawAxisTitles(context, chart, plot, area, horizontal: false);
    }

    // ---- Radar ------------------------------------------------------------

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

    // ---- Legend -----------------------------------------------------------

    private sealed record LegendModel(IReadOnlyList<(string Name, int Index)> Items, double Height);

    private static LegendModel BuildLegend(ResolvedChart chart)
    {
        // Pie/doughnut draw a single series as slices, so the legend names the slices (categories),
        // colour-keyed by slice index to match DrawPie — not the lone, unhelpful series name.
        if (chart.Type is ChartType.Pie or ChartType.Doughnut)
        {
            var series = chart.Series.Count > 0 ? chart.Series[0] : null;
            var slices = new List<(string Name, int Index)>();
            if (series is not null)
            {
                for (int i = 0; i < series.Values.Count; i++)
                {
                    double v = series.Values[i];
                    if (double.IsNaN(v) || v <= 0)
                    {
                        continue; // matches the slices DrawPie actually draws
                    }

                    string label = i < chart.Categories.Count ? chart.Categories[i] : (i + 1).ToString(CultureInfo.InvariantCulture);
                    slices.Add((label, i));
                }
            }

            return new LegendModel(slices, 26);
        }

        var items = chart.Series.Select((s, i) => (s.Name, i)).ToList();
        return new LegendModel(items, 26);
    }

    private void DrawLegend(DrawingContext context, LegendModel legend, Rect area)
    {
        var measured = legend.Items.Select(item => (item, ft: Text(item.Name, 11, TextBrush))).ToList();
        double spacing = 18;
        double total = measured.Sum(m => 14 + 4 + m.ft.Width) + spacing * (measured.Count - 1);
        double x = area.X + Math.Max(0, (area.Width - total) / 2);
        double y = area.Y + (area.Height - 12) / 2;

        foreach (var (item, ft) in measured)
        {
            context.FillRectangle(SeriesBrush(item.Index), new Rect(x, y, 12, 12));
            context.DrawText(ft, new Point(x + 16, y + 6 - ft.Height / 2));
            x += 14 + 4 + ft.Width + spacing;
        }
    }

    // ---- Shared helpers ---------------------------------------------------

    private static int IndexOf(ResolvedChart chart, ResolvedSeries series)
    {
        for (int i = 0; i < chart.Series.Count; i++)
        {
            if (ReferenceEquals(chart.Series[i], series))
            {
                return i;
            }
        }

        return 0;
    }

    private static double ValueAt(ResolvedSeries series, int category) =>
        category < series.Values.Count ? series.Values[category] : double.NaN;

    private static double PositiveSum(IEnumerable<ResolvedSeries> series, int category) =>
        series.Select(s => ValueAt(s, category)).Where(v => !double.IsNaN(v) && v > 0).Sum();

    private static (double Min, double Max) ValueExtent(ResolvedChart chart, int categoryCount, bool stacked, bool percent)
    {
        if (percent)
        {
            return (0, 1);
        }

        double min = 0, max = 0;
        var bars = chart.Series.Where(s => s.Type is ChartType.Column or ChartType.Bar).ToList();
        var others = chart.Series.Where(s => s.Type is not (ChartType.Column or ChartType.Bar)).ToList();

        for (int c = 0; c < categoryCount; c++)
        {
            if (stacked && bars.Count > 0)
            {
                double pos = 0, neg = 0;
                foreach (var s in bars)
                {
                    double v = ValueAt(s, c);
                    if (double.IsNaN(v))
                    {
                        continue;
                    }
                    if (v >= 0)
                    {
                        pos += v;
                    }
                    else
                    {
                        neg += v;
                    }
                }

                max = Math.Max(max, pos);
                min = Math.Min(min, neg);
            }
            else
            {
                foreach (var s in bars)
                {
                    double v = ValueAt(s, c);
                    if (double.IsNaN(v))
                    {
                        continue;
                    }
                    max = Math.Max(max, v);
                    min = Math.Min(min, v);
                }
            }

            foreach (var s in others)
            {
                double v = ValueAt(s, c);
                if (double.IsNaN(v))
                {
                    continue;
                }
                max = Math.Max(max, v);
                min = Math.Min(min, v);
            }
        }

        if (max <= min)
        {
            max = min + 1;
        }

        return (min, max);
    }

    private static double ValuePos(double value, double min, double max, Rect area, bool horizontal)
    {
        double t = (value - min) / (max - min);
        return horizontal ? area.X + t * area.Width : area.Bottom - t * area.Height;
    }

    private static IBrush SeriesBrush(int index) => new SolidColorBrush(Palette[index % Palette.Length]);

    private static string FormatNumber(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 1000)
        {
            return value.ToString("#,##0", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static FormattedText Text(string text, double size, IBrush brush, bool bold = false) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal),
        size,
        brush);

    private static void DrawCentered(DrawingContext context, string message, Rect area, IBrush brush, double size)
    {
        var ft = Text(message, size, brush);
        context.DrawText(ft, new Point(area.X + (area.Width - ft.Width) / 2, area.Y + (area.Height - ft.Height) / 2));
    }
}
