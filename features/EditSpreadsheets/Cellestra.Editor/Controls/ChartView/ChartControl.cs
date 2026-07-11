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
public sealed partial class ChartControl : Control
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

    // ---- Legend model -----------------------------------------------------

    private sealed record LegendModel(IReadOnlyList<(string Name, int Index)> Items, double Height);

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
