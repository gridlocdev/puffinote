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
    public static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    /// <summary>The ODF media type of an embedded chart sub-document (its object directory).</summary>
    public const string ChartMediaType = "application/vnd.oasis.opendocument.chart";

    // ---- table:database-range (filter + sort round-trip, ODF §9.4.x) ----
    public const string DatabaseRanges = "database-ranges";
    public const string DatabaseRange = "database-range";
    public const string TargetRangeAddress = "target-range-address";
    public const string DisplayFilterButtons = "display-filter-buttons";
    public const string Sort = "sort";
    public const string SortBy = "sort-by";
    public const string FieldNumber = "field-number";
    public const string Order = "order";
    public const string Filter = "filter";
    public const string FilterAnd = "filter-and";
    public const string FilterOr = "filter-or";
    public const string FilterCondition = "filter-condition";
    public const string Operator = "operator";
    public const string DataType = "data-type";
    public const string ContainsHeader = "contains-header";

    // ---- table:content-validation (data validation round-trip, ODF §9.4.5) ----
    public const string ContentValidations = "content-validations";
    public const string ContentValidation = "content-validation";
    public const string ContentValidationName = "content-validation-name";
    public const string Condition = "condition";
    public const string BaseCellAddress = "base-cell-address";
    public const string AllowEmptyCell = "allow-empty-cell";
    public const string DisplayList = "display-list";
    public const string ErrorMessage = "error-message";
    public const string HelpMessage = "help-message";
    public const string MessageType = "message-type";
    public const string Display = "display";
    public const string Title = "title";

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
