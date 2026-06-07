using System.Xml.Linq;
using Cellestra.Core.Model;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>Builds the smaller ODF parts: meta.xml, settings.xml, styles.xml, and the manifest.</summary>
public static class OdsSupportXml
{
    private const string CalcModeItem = "CellestraCalculationMode";
    private const string ActiveTableItem = "ActiveTable";

    public static XDocument BuildMeta()
    {
        var meta = new XElement(Office + "meta",
            new XElement(XNamespace.Get("http://purl.org/dc/elements/1.1/") + "date"),
            new XElement(Office + "generator", "Cellestra/1.0"));

        var root = new XElement(Office + "document-meta",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", "http://purl.org/dc/elements/1.1/"),
            new XAttribute(Office + "version", "1.3"),
            meta);

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    public static XDocument BuildStyles()
    {
        var root = new XElement(Office + "document-styles",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", Fo.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            new XElement(Office + "styles"),
            new XElement(Office + "automatic-styles"),
            new XElement(Office + "master-styles"));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    public static XDocument BuildSettings(Workbook workbook)
    {
        string activeSheet = workbook.SheetCount > 0
            ? workbook.Worksheets[Math.Clamp(workbook.Settings.ActiveSheetIndex, 0, workbook.SheetCount - 1)].Name
            : string.Empty;

        var itemSet = new XElement(Config + "config-item-set",
            new XAttribute(Config + "name", "ooo:view-settings"),
            ConfigItem(ActiveTableItem, "string", activeSheet),
            ConfigItem(CalcModeItem, "string", workbook.Settings.CalculationMode.ToString()));

        var root = new XElement(Office + "document-settings",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "config", Config.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            new XElement(Office + "settings", itemSet));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    public static void ApplySettings(string settingsXml, Workbook workbook)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(settingsXml);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        foreach (var item in document.Descendants(Config + "config-item"))
        {
            string? name = item.Attribute(Config + "name")?.Value;
            if (name == CalcModeItem && Enum.TryParse<CalculationMode>(item.Value, out var mode))
            {
                workbook.Settings.CalculationMode = mode;
            }
            else if (name == ActiveTableItem)
            {
                var sheet = workbook.FindSheet(item.Value);
                if (sheet is not null)
                {
                    workbook.Settings.ActiveSheetIndex = workbook.IndexOf(sheet);
                }
            }
        }
    }

    public static XDocument BuildManifest(IEnumerable<string> parts)
    {
        var manifest = new XElement(Manifest + "manifest",
            new XAttribute(XNamespace.Xmlns + "manifest", Manifest.NamespaceName),
            new XAttribute(Manifest + "version", "1.3"));

        manifest.Add(FileEntry("/", MimeType));
        foreach (string part in parts)
        {
            if (part == MimetypePart)
            {
                continue;
            }

            manifest.Add(FileEntry(part, MediaTypeFor(part)));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), manifest);
    }

    private static XElement FileEntry(string path, string mediaType) =>
        new(Manifest + "file-entry",
            new XAttribute(Manifest + "full-path", path),
            new XAttribute(Manifest + "media-type", mediaType));

    private static XElement ConfigItem(string name, string type, string value) =>
        new(Config + "config-item",
            new XAttribute(Config + "name", name),
            new XAttribute(Config + "type", type),
            value);

    private static string MediaTypeFor(string part) => part switch
    {
        _ when part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) => "text/xml",
        _ when part.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
        _ when part.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               part.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) => "image/jpeg",
        _ => "application/octet-stream",
    };
}
