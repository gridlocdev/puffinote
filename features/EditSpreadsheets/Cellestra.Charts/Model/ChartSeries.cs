namespace Cellestra.Charts;

/// <summary>
/// One plotted data series. Ranges are stored as ODF address strings (e.g. <c>Data.B2:B7</c>) so
/// this library needs no dependency on the spreadsheet model; the host resolves them to numbers.
/// A series may override the chart's <see cref="ChartType"/> via <see cref="Type"/>, which is how a
/// combo chart (e.g. columns + a line) is expressed — exactly as ODF assigns a <c>chart:class</c>
/// per <c>chart:series</c>.
/// </summary>
public sealed class ChartSeries
{
    /// <summary>A literal series name, used when <see cref="NameReference"/> is not set.</summary>
    public string? Name { get; set; }

    /// <summary>A single-cell reference whose text is the series name (ODF <c>chart:label-cell-address</c>).</summary>
    public string? NameReference { get; set; }

    /// <summary>The range of Y values for the series (ODF <c>chart:values-cell-range-address</c>).</summary>
    public string ValuesReference { get; set; } = string.Empty;

    /// <summary>X values for an X/Y series (scatter/bubble); ODF <c>chart:domain</c>. Null otherwise.</summary>
    public string? XValuesReference { get; set; }

    /// <summary>Bubble sizes for a bubble series; a second values range. Null otherwise.</summary>
    public string? BubbleSizeReference { get; set; }

    /// <summary>Per-series type override for combo charts; null means "use the chart's type".</summary>
    public ChartType? Type { get; set; }

    public ChartSeries Clone() => new()
    {
        Name = Name,
        NameReference = NameReference,
        ValuesReference = ValuesReference,
        XValuesReference = XValuesReference,
        BubbleSizeReference = BubbleSizeReference,
        Type = Type,
    };
}
