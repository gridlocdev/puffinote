namespace Cellestra.Charts;

/// <summary>
/// The chart kinds Cellestra draws and round-trips through ODF. Each maps to an ODF
/// <c>chart:class</c> (see <see cref="ChartTypes"/>). Note that <see cref="Column"/> and
/// <see cref="Bar"/> share one ODF class (<c>chart:bar</c>) and differ only by orientation —
/// ODF expresses that as the <c>chart:vertical</c> plot-area flag, not a distinct class.
/// Stacking and 100%-stacking are likewise a property (<see cref="ChartStacking"/>), not a type.
/// A combo chart falls out of letting each <see cref="ChartSeries"/> override its own type.
/// </summary>
public enum ChartType
{
    /// <summary>Vertical bars (ODF <c>chart:bar</c>, not vertical).</summary>
    Column,

    /// <summary>Horizontal bars (ODF <c>chart:bar</c>, vertical).</summary>
    Bar,

    /// <summary>A line per series (ODF <c>chart:line</c>).</summary>
    Line,

    /// <summary>A filled line per series (ODF <c>chart:area</c>).</summary>
    Area,

    /// <summary>A single-series pie (ODF <c>chart:circle</c>).</summary>
    Pie,

    /// <summary>A single-series ring/doughnut (ODF <c>chart:ring</c>).</summary>
    Doughnut,

    /// <summary>An X/Y point cloud (ODF <c>chart:scatter</c>).</summary>
    Scatter,

    /// <summary>An X/Y chart with a third value sizing each bubble (ODF <c>chart:bubble</c>).</summary>
    Bubble,

    /// <summary>A spider/star chart (ODF <c>chart:radar</c>).</summary>
    Radar,

    /// <summary>A filled spider/star chart (ODF <c>chart:filled-radar</c>).</summary>
    FilledRadar,
}

/// <summary>Maps <see cref="ChartType"/> to and from its ODF <c>chart:class</c> + orientation.</summary>
public static class ChartTypes
{
    /// <summary>The ODF <c>chart:class</c> string for a type (e.g. <c>chart:bar</c>).</summary>
    public static string OdsClass(ChartType type) => type switch
    {
        ChartType.Column or ChartType.Bar => "chart:bar",
        ChartType.Line => "chart:line",
        ChartType.Area => "chart:area",
        ChartType.Pie => "chart:circle",
        ChartType.Doughnut => "chart:ring",
        ChartType.Scatter => "chart:scatter",
        ChartType.Bubble => "chart:bubble",
        ChartType.Radar => "chart:radar",
        ChartType.FilledRadar => "chart:filled-radar",
        _ => "chart:bar",
    };

    /// <summary>True when bars run horizontally (ODF <c>chart:vertical="true"</c>); only <see cref="ChartType.Bar"/>.</summary>
    public static bool IsHorizontalBars(ChartType type) => type == ChartType.Bar;

    /// <summary>Recover a type from an ODF class and the plot-area's <c>chart:vertical</c> flag.</summary>
    public static ChartType FromOds(string? odsClass, bool vertical) => Normalize(odsClass) switch
    {
        "chart:bar" => vertical ? ChartType.Bar : ChartType.Column,
        "chart:line" => ChartType.Line,
        "chart:area" => ChartType.Area,
        "chart:circle" => ChartType.Pie,
        "chart:ring" => ChartType.Doughnut,
        "chart:scatter" => ChartType.Scatter,
        "chart:bubble" => ChartType.Bubble,
        "chart:radar" => ChartType.Radar,
        "chart:filled-radar" => ChartType.FilledRadar,
        _ => ChartType.Column,
    };

    /// <summary>True for the category-axis families (column/bar/line/area/radar); false for pie/ring/scatter/bubble.</summary>
    public static bool UsesCategoryAxis(ChartType type) => type is
        ChartType.Column or ChartType.Bar or ChartType.Line or ChartType.Area
        or ChartType.Radar or ChartType.FilledRadar;

    /// <summary>True for the families that honour <see cref="ChartStacking"/> (column/bar/area).</summary>
    public static bool SupportsStacking(ChartType type) => type is
        ChartType.Column or ChartType.Bar or ChartType.Area;

    /// <summary>True for the X/Y families that carry their own X values (scatter/bubble).</summary>
    public static bool IsXY(ChartType type) => type is ChartType.Scatter or ChartType.Bubble;

    private static string Normalize(string? odsClass)
    {
        if (string.IsNullOrEmpty(odsClass))
        {
            return string.Empty;
        }

        // Tolerate an unprefixed class ("bar") as well as the canonical "chart:bar".
        return odsClass.Contains(':') ? odsClass : "chart:" + odsClass;
    }
}
