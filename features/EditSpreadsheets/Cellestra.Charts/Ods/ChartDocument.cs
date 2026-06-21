using System.Globalization;
using System.Xml.Linq;
using static Cellestra.Charts.Ods.ChartXmlNames;

namespace Cellestra.Charts.Ods;

/// <summary>
/// A from-scratch reader/writer for an embedded ODF chart object's <c>content.xml</c> — the
/// <c>&lt;office:chart&gt;&lt;chart:chart&gt;</c> tree. No charting library is involved; this builds
/// and parses the XML by hand so the model maps exactly onto the ODF the spec calls for:
/// <list type="bullet">
///   <item>the chart family is the <c>chart:class</c> on <c>&lt;chart:chart&gt;</c>;</item>
///   <item>column-vs-bar is the <c>chart:vertical</c> flag on the plot area;</item>
///   <item>stacking / 100%-stacking are the <c>chart:stacked</c> / <c>chart:percentage</c> flags;</item>
///   <item>a combo is per-series <c>chart:class</c> on each <c>&lt;chart:series&gt;</c>.</item>
/// </list>
/// </summary>
public static class ChartDocument
{
    /// <summary>The default frame size for a chart sheet (the whole "page"), in centimetres.</summary>
    public const double DefaultWidthCm = 24.0;
    public const double DefaultHeightCm = 14.0;

    /// <summary>A private attribute for a literal series name (no ODF equivalent exists).</summary>
    private static readonly XName CellestraName = XNamespace.Get("urn:cellestra:chart") + "name";

    // ---- Write ------------------------------------------------------------

    /// <summary>Build the <c>content.xml</c> document for an embedded chart object.</summary>
    public static XDocument BuildContent(ChartDefinition chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var plotArea = new XElement(Chart + "plot-area");
        bool stacked = chart.Stacking is ChartStacking.Stacked or ChartStacking.Percent
            && ChartTypes.SupportsStacking(chart.Type);
        bool percentage = chart.Stacking is ChartStacking.Percent && ChartTypes.SupportsStacking(chart.Type);

        if (stacked)
        {
            plotArea.Add(new XAttribute(Chart + "stacked", "true"));
        }

        if (percentage)
        {
            plotArea.Add(new XAttribute(Chart + "percentage", "true"));
        }

        if (chart.Type == ChartType.Bar)
        {
            // ODF: bars run horizontally when the plot area is "vertical".
            plotArea.Add(new XAttribute(Chart + "vertical", "true"));
        }

        // X / category axis (carries the categories range, ODF §18).
        var xAxis = new XElement(Chart + "axis",
            new XAttribute(Chart + "dimension", "x"),
            new XAttribute(Chart + "name", "primary-x"));
        if (!string.IsNullOrEmpty(chart.XAxisTitle))
        {
            xAxis.Add(Title(chart.XAxisTitle));
        }

        if (!string.IsNullOrWhiteSpace(chart.CategoriesReference))
        {
            xAxis.Add(new XElement(Chart + "categories",
                new XAttribute(Table + "cell-range-address", chart.CategoriesReference)));
        }

        plotArea.Add(xAxis);

        // Y / value axis.
        var yAxis = new XElement(Chart + "axis",
            new XAttribute(Chart + "dimension", "y"),
            new XAttribute(Chart + "name", "primary-y"));
        if (!string.IsNullOrEmpty(chart.YAxisTitle))
        {
            yAxis.Add(Title(chart.YAxisTitle));
        }

        plotArea.Add(yAxis);

        foreach (var series in chart.Series)
        {
            plotArea.Add(BuildSeries(series, chart.Type));
        }

        var chartElement = new XElement(Chart + "chart",
            new XAttribute(Chart + "class", ChartTypes.OdsClass(chart.Type)),
            new XAttribute(Svg + "width", Cm(DefaultWidthCm)),
            new XAttribute(Svg + "height", Cm(DefaultHeightCm)));

        if (!string.IsNullOrEmpty(chart.Title))
        {
            chartElement.Add(Title(chart.Title));
        }

        if (chart.ShowLegend)
        {
            chartElement.Add(new XElement(Chart + "legend",
                new XAttribute(Chart + "legend-position", "end")));
        }

        chartElement.Add(plotArea);

        var root = new XElement(Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "chart", Chart.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", Table.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "svg", Svg.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            new XElement(Office + "body",
                new XElement(Office + "chart", chartElement)));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement BuildSeries(ChartSeries series, ChartType chartType)
    {
        var type = series.Type ?? chartType;
        var element = new XElement(Chart + "series",
            new XAttribute(Chart + "class", ChartTypes.OdsClass(type)),
            new XAttribute(Chart + "values-cell-range-address", series.ValuesReference));

        if (!string.IsNullOrWhiteSpace(series.NameReference))
        {
            element.Add(new XAttribute(Chart + "label-cell-address", series.NameReference));
        }
        else if (!string.IsNullOrEmpty(series.Name))
        {
            // ODF has no attribute for a literal (non-cell) series name, so keep it on a
            // Cellestra-namespaced attribute. Conforming readers ignore unknown-namespace attributes,
            // so this round-trips for us without confusing LibreOffice.
            element.SetAttributeValue(CellestraName, series.Name);
        }

        if (!string.IsNullOrWhiteSpace(series.XValuesReference))
        {
            element.Add(new XElement(Chart + "domain",
                new XAttribute(Table + "cell-range-address", series.XValuesReference)));
        }

        return element;
    }

    private static XElement Title(string text) =>
        new(Chart + "title", new XElement(Text + "p", text));

    private static string Cm(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + "cm";

    // ---- Read -------------------------------------------------------------

    /// <summary>Parse an embedded chart object's <c>content.xml</c> back into a <see cref="ChartDefinition"/>.</summary>
    public static ChartDefinition? Parse(string contentXml)
    {
        if (string.IsNullOrWhiteSpace(contentXml))
        {
            return null;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(contentXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var chartElement = document.Root?
            .Element(Office + "body")?
            .Element(Office + "chart")?
            .Element(Chart + "chart");
        if (chartElement is null)
        {
            return null;
        }

        var plotArea = chartElement.Element(Chart + "plot-area");
        bool vertical = plotArea?.Attribute(Chart + "vertical")?.Value == "true";
        var type = ChartTypes.FromOds(chartElement.Attribute(Chart + "class")?.Value, vertical);

        bool stacked = plotArea?.Attribute(Chart + "stacked")?.Value == "true";
        bool percentage = plotArea?.Attribute(Chart + "percentage")?.Value == "true";

        var chart = new ChartDefinition
        {
            Type = type,
            Title = TitleText(chartElement) ?? string.Empty,
            ShowLegend = chartElement.Element(Chart + "legend") is not null,
            Stacking = percentage ? ChartStacking.Percent : stacked ? ChartStacking.Stacked : ChartStacking.None,
        };

        if (plotArea is null)
        {
            return chart;
        }

        foreach (var axis in plotArea.Elements(Chart + "axis"))
        {
            string? dimension = axis.Attribute(Chart + "dimension")?.Value;
            string? title = TitleText(axis);
            if (dimension == "x")
            {
                chart.XAxisTitle = title;
                var categories = axis.Element(Chart + "categories")?.Attribute(Table + "cell-range-address")?.Value;
                if (!string.IsNullOrWhiteSpace(categories))
                {
                    chart.CategoriesReference = categories;
                }
            }
            else if (dimension == "y")
            {
                chart.YAxisTitle = title;
            }
        }

        foreach (var seriesElement in plotArea.Elements(Chart + "series"))
        {
            var seriesType = ChartTypes.FromOds(seriesElement.Attribute(Chart + "class")?.Value, vertical);
            var series = new ChartSeries
            {
                ValuesReference = seriesElement.Attribute(Chart + "values-cell-range-address")?.Value ?? string.Empty,
                NameReference = seriesElement.Attribute(Chart + "label-cell-address")?.Value,
                Name = seriesElement.Attribute(CellestraName)?.Value,
                XValuesReference = seriesElement.Element(Chart + "domain")?.Attribute(Table + "cell-range-address")?.Value,
                Type = seriesType == type ? null : seriesType,
            };
            chart.Series.Add(series);
        }

        return chart;
    }

    private static string? TitleText(XElement parent)
    {
        var title = parent.Element(Chart + "title");
        var text = title?.Element(Text + "p")?.Value;
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
