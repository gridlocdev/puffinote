using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Cellestra.Charts;
using Cellestra.Core.Charts;

namespace Cellestra.Desktop.Controls.ChartView;

/// <summary>
/// The chart legend: a centred, single-row strip of colour swatches with labels. Pie/doughnut
/// charts key the legend to slice (category) colours to match <c>DrawPie</c>; every other type keys
/// it to series.
/// </summary>
public sealed partial class ChartControl
{
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
}
