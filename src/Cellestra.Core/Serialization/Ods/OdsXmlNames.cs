using System.Xml.Linq;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>The ODF XML namespaces and the part names Cellestra reads and writes.</summary>
public static class OdsXmlNames
{
    public static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    public static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    public static readonly XNamespace Number = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";
    public static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    public static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    public static readonly XNamespace Config = "urn:oasis:names:tc:opendocument:xmlns:config:1.0";
    public static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";

    public const string MimeType = "application/vnd.oasis.opendocument.spreadsheet";

    public const string MimetypePart = "mimetype";
    public const string ContentPart = "content.xml";
    public const string StylesPart = "styles.xml";
    public const string MetaPart = "meta.xml";
    public const string SettingsPart = "settings.xml";
    public const string ManifestPart = "META-INF/manifest.xml";

    /// <summary>The parts Cellestra regenerates from the model on save (everything else is preserved verbatim).</summary>
    public static readonly IReadOnlySet<string> GeneratedParts = new HashSet<string>(StringComparer.Ordinal)
    {
        MimetypePart, ContentPart, StylesPart, MetaPart, SettingsPart, ManifestPart,
    };
}
