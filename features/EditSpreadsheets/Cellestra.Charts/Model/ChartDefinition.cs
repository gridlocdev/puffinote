namespace Cellestra.Charts;

/// <summary>
/// A whole chart: its type, title, axes, legend, stacking, the category (label) range, and one or
/// more <see cref="ChartSeries"/>. This is the unit a "chart sheet" stores. It is a pure data model
/// — drawing lives in the desktop renderer, persistence in <see cref="Cellestra.Charts.Ods"/> —
/// and is mutated in place by the chart sheet's editing pane.
/// </summary>
public sealed class ChartDefinition
{
    /// <summary>The chart heading. Empty hides the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The chart type applied to series that don't override it.</summary>
    public ChartType Type { get; set; } = ChartType.Column;

    /// <summary>Stacking for the column/bar/area families; ignored by the others.</summary>
    public ChartStacking Stacking { get; set; } = ChartStacking.None;

    /// <summary>The range of category labels shared by every series (the X/category axis).</summary>
    public string? CategoriesReference { get; set; }

    /// <summary>The plotted series, drawn in order.</summary>
    public List<ChartSeries> Series { get; } = new();

    /// <summary>Whether to draw the legend.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Optional category/X axis title.</summary>
    public string? XAxisTitle { get; set; }

    /// <summary>Optional value/Y axis title.</summary>
    public string? YAxisTitle { get; set; }

    /// <summary>
    /// The ODF embedded-object directory this chart serializes into (e.g. <c>Object 1</c>). Assigned
    /// when the chart is first created or read so a save reuses the same href and leaves no orphaned
    /// part behind on round-trip. Empty until assigned by the serializer.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    public ChartDefinition Clone()
    {
        var copy = new ChartDefinition
        {
            Title = Title,
            Type = Type,
            Stacking = Stacking,
            CategoriesReference = CategoriesReference,
            ShowLegend = ShowLegend,
            XAxisTitle = XAxisTitle,
            YAxisTitle = YAxisTitle,
            ObjectName = ObjectName,
        };
        foreach (var series in Series)
        {
            copy.Series.Add(series.Clone());
        }

        return copy;
    }
}
