namespace Cellestra.Charts;

/// <summary>
/// How a column/bar/area chart combines its series. ODF expresses this as two plot-area
/// flags rather than a chart class: <c>chart:stacked</c> and <c>chart:percentage</c>.
/// </summary>
public enum ChartStacking
{
    /// <summary>Series sit side by side (neither flag set).</summary>
    None,

    /// <summary>Series stack on top of one another (<c>chart:stacked="true"</c>).</summary>
    Stacked,

    /// <summary>Series stack and each category fills to 100% (<c>chart:stacked</c> + <c>chart:percentage</c>).</summary>
    Percent,
}
