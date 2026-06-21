using System.Globalization;
using Cellestra.Charts;
using Cellestra.Core.Model;

namespace Cellestra.Core.Charts;

/// <summary>One series resolved to concrete numbers ready to plot.</summary>
/// <param name="Name">The display name (resolved from a literal name, a name cell, or a fallback).</param>
/// <param name="Type">The effective chart type for this series (its override, or the chart's type).</param>
/// <param name="Values">The Y values; <see cref="double.NaN"/> marks a blank/non-numeric cell (a gap).</param>
/// <param name="XValues">X values for an X/Y series (scatter/bubble), or null.</param>
/// <param name="BubbleSizes">Bubble sizes for a bubble series, or null.</param>
public sealed record ResolvedSeries(
    string Name,
    ChartType Type,
    IReadOnlyList<double> Values,
    IReadOnlyList<double>? XValues,
    IReadOnlyList<double>? BubbleSizes);

/// <summary>A whole chart resolved to plottable data, plus the presentation flags the renderer needs.</summary>
public sealed record ResolvedChart(
    string Title,
    ChartType Type,
    ChartStacking Stacking,
    bool ShowLegend,
    string? XAxisTitle,
    string? YAxisTitle,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ResolvedSeries> Series)
{
    /// <summary>True when there is nothing to draw (no series, or every series is empty).</summary>
    public bool IsEmpty => Series.Count == 0 || Series.All(s => s.Values.Count == 0);
}

/// <summary>
/// Resolves a <see cref="ChartDefinition"/>'s stored range strings (e.g. <c>Data.B2:B7</c>) against a
/// <see cref="Workbook"/> into numbers and labels. Pure logic in Core (no UI), so it is unit-tested
/// directly and reused by the chart renderer. Unqualified references default to the first data sheet.
/// </summary>
public static class ChartDataResolver
{
    public static ResolvedChart Resolve(Workbook workbook, ChartDefinition chart)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(chart);

        string fallbackSheet = DefaultSheetName(workbook);
        var categories = ReadLabels(workbook, chart.CategoriesReference, fallbackSheet);

        var series = new List<ResolvedSeries>();
        int index = 1;
        foreach (var definition in chart.Series)
        {
            var values = ReadNumbers(workbook, definition.ValuesReference, fallbackSheet);
            var xValues = ReadNumbers(workbook, definition.XValuesReference, fallbackSheet);
            var bubbleSizes = ReadNumbers(workbook, definition.BubbleSizeReference, fallbackSheet);
            string name = ResolveName(workbook, definition, fallbackSheet, index);
            series.Add(new ResolvedSeries(
                name,
                definition.Type ?? chart.Type,
                values,
                xValues.Count > 0 ? xValues : null,
                bubbleSizes.Count > 0 ? bubbleSizes : null));
            index++;
        }

        return new ResolvedChart(
            chart.Title,
            chart.Type,
            chart.Stacking,
            chart.ShowLegend,
            chart.XAxisTitle,
            chart.YAxisTitle,
            categories,
            series);
    }

    private static string ResolveName(Workbook workbook, ChartSeries series, string fallbackSheet, int index)
    {
        if (!string.IsNullOrWhiteSpace(series.NameReference) &&
            CellRange.TryParse(Qualify(series.NameReference, fallbackSheet), out var range) &&
            workbook.FindSheet(range.Sheet ?? fallbackSheet) is { } sheet)
        {
            var (row, column) = (range.Top, range.Left);
            string text = LabelOf(sheet.GetValue(row, column));
            if (text.Length > 0)
            {
                return text;
            }
        }

        if (!string.IsNullOrEmpty(series.Name))
        {
            return series.Name!;
        }

        return $"Series {index}";
    }

    private static IReadOnlyList<double> ReadNumbers(Workbook workbook, string? reference, string fallbackSheet)
    {
        if (string.IsNullOrWhiteSpace(reference) ||
            !CellRange.TryParse(Qualify(reference, fallbackSheet), out var range) ||
            workbook.FindSheet(range.Sheet ?? fallbackSheet) is not { } sheet)
        {
            return Array.Empty<double>();
        }

        var values = new List<double>(range.CellCount);
        foreach (var (row, column) in range.Positions())
        {
            var value = sheet.GetValue(row, column);
            values.Add(value.IsNumber || value.IsBoolean ? value.AsNumber : double.NaN);
        }

        return values;
    }

    private static IReadOnlyList<string> ReadLabels(Workbook workbook, string? reference, string fallbackSheet)
    {
        if (string.IsNullOrWhiteSpace(reference) ||
            !CellRange.TryParse(Qualify(reference, fallbackSheet), out var range) ||
            workbook.FindSheet(range.Sheet ?? fallbackSheet) is not { } sheet)
        {
            return Array.Empty<string>();
        }

        var labels = new List<string>(range.CellCount);
        foreach (var (row, column) in range.Positions())
        {
            labels.Add(LabelOf(sheet.GetValue(row, column)));
        }

        return labels;
    }

    private static string LabelOf(Values.CellValue value)
    {
        if (value.IsText)
        {
            return value.AsText;
        }

        if (value.IsNumber)
        {
            return value.AsNumber.ToString("0.######", CultureInfo.InvariantCulture);
        }

        if (value.IsBoolean)
        {
            return value.AsBoolean ? "TRUE" : "FALSE";
        }

        return string.Empty;
    }

    /// <summary>Qualify a sheet-less reference with the fallback sheet so it resolves to a real sheet.</summary>
    private static string Qualify(string reference, string fallbackSheet)
    {
        reference = reference.Trim();
        return reference.Contains('.') ? reference : $"{fallbackSheet}.{reference}";
    }

    /// <summary>The sheet an unqualified reference targets: the first data (non-chart) sheet, else the first sheet.</summary>
    private static string DefaultSheetName(Workbook workbook)
    {
        foreach (var sheet in workbook.Worksheets)
        {
            if (!sheet.IsChartSheet)
            {
                return sheet.Name;
            }
        }

        return workbook.Worksheets.Count > 0 ? workbook.Worksheets[0].Name : Defaults.FirstSheetName;
    }
}
