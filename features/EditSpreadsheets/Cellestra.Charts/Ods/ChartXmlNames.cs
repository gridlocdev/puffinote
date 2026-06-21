using System.Xml.Linq;

namespace Cellestra.Charts.Ods;

/// <summary>The ODF namespaces used inside an embedded chart object's <c>content.xml</c>.</summary>
public static class ChartXmlNames
{
    public static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public static readonly XNamespace Chart = "urn:oasis:names:tc:opendocument:xmlns:chart:1.0";
    public static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    /// <summary>The ODF media type of an embedded chart sub-document.</summary>
    public const string ChartMediaType = "application/vnd.oasis.opendocument.chart";
}
