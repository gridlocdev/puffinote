using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Model;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>Builds the smaller ODF parts: meta.xml, settings.xml, styles.xml, and the manifest.</summary>
public static class OdsSupportXml
{
    private const string CalcModeItem = "CellestraCalculationMode";
    private const string ActiveTableItem = "ActiveTable";
    private const string ViewsItem = "Views";
    private const string ViewIdItem = "ViewId";
    private const string TablesItem = "Tables";
    private const string HorizontalSplitModeItem = "HorizontalSplitMode";
    private const string VerticalSplitModeItem = "VerticalSplitMode";
    private const string HorizontalSplitPositionItem = "HorizontalSplitPosition";
    private const string VerticalSplitPositionItem = "VerticalSplitPosition";
    private const string PositionRightItem = "PositionRight";
    private const string PositionBottomItem = "PositionBottom";

    /// <summary>ODF/LibreOffice split mode: 0 none, 1 a movable split, 2 a freeze. Only 2 is a freeze.</summary>
    private const string FrozenSplitMode = "2";
    private const string NoSplitMode = "0";

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

        // The frozen panes and the active sheet live where LibreOffice looks for them: inside a view
        // in ooo:view-settings, with one Tables entry per sheet that has a freeze. Freezing used to be
        // session-only — it survived neither a save nor `cellestra clean-up`, which freezes a header
        // row and then writes the file.
        var view = new XElement(Config + "config-item-map-entry",
            ConfigItem(ViewIdItem, "string", "view1"),
            Tables(workbook),
            ConfigItem(ActiveTableItem, "string", activeSheet));

        var itemSet = new XElement(Config + "config-item-set",
            new XAttribute(Config + "name", "ooo:view-settings"),
            new XElement(Config + "config-item-map-indexed",
                new XAttribute(Config + "name", ViewsItem),
                view),
            ConfigItem(CalcModeItem, "string", workbook.Settings.CalculationMode.ToString()));

        var root = new XElement(Office + "document-settings",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "config", Config.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            new XElement(Office + "settings", itemSet));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    /// <summary>
    /// One <c>Tables</c> map holding the frozen-pane state of every sheet that has one. Sheets with no
    /// freeze are left out rather than written as zeroes — an absent entry already means "no panes".
    /// </summary>
    private static XElement Tables(Workbook workbook)
    {
        var tables = new XElement(Config + "config-item-map-named", new XAttribute(Config + "name", TablesItem));
        foreach (var sheet in workbook.Worksheets)
        {
            if (sheet.FrozenRows <= 0 && sheet.FrozenColumns <= 0)
            {
                continue;
            }

            tables.Add(new XElement(Config + "config-item-map-entry",
                new XAttribute(Config + "name", sheet.Name),
                ConfigItem(HorizontalSplitModeItem, "short", sheet.FrozenColumns > 0 ? FrozenSplitMode : NoSplitMode),
                ConfigItem(VerticalSplitModeItem, "short", sheet.FrozenRows > 0 ? FrozenSplitMode : NoSplitMode),
                ConfigItem(HorizontalSplitPositionItem, "int", Count(sheet.FrozenColumns)),
                ConfigItem(VerticalSplitPositionItem, "int", Count(sheet.FrozenRows)),
                // Where the unfrozen pane starts — the same line, since a freeze splits exactly there.
                ConfigItem(PositionRightItem, "int", Count(sheet.FrozenColumns)),
                ConfigItem(PositionBottomItem, "int", Count(sheet.FrozenRows))));
        }

        return tables;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

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

        foreach (var entry in document.Descendants(Config + "config-item-map-entry"))
        {
            if (entry.Attribute(Config + "name")?.Value is not { } sheetName ||
                workbook.FindSheet(sheetName) is not { } sheet)
            {
                continue;
            }

            sheet.FrozenColumns = FrozenLines(entry, HorizontalSplitModeItem, HorizontalSplitPositionItem);
            sheet.FrozenRows = FrozenLines(entry, VerticalSplitModeItem, VerticalSplitPositionItem);
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

    /// <summary>
    /// How many lines a pane holds frozen on one axis: the position, but only when the mode says the
    /// split is a freeze. A mode-1 split is a scroll divider the user can drag, not frozen headers,
    /// and reading it as one would lock rows nobody asked to lock.
    /// </summary>
    private static int FrozenLines(XElement entry, string modeItem, string positionItem)
    {
        if (ItemValue(entry, modeItem) != FrozenSplitMode)
        {
            return 0;
        }

        return int.TryParse(ItemValue(entry, positionItem), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lines) && lines > 0
            ? lines
            : 0;
    }

    private static string? ItemValue(XElement entry, string name) =>
        entry.Elements(Config + "config-item")
            .FirstOrDefault(item => item.Attribute(Config + "name")?.Value == name)?.Value;

    public static XDocument BuildManifest(IEnumerable<string> parts, IEnumerable<string>? chartObjectDirs = null)
    {
        var manifest = new XElement(Manifest + "manifest",
            new XAttribute(XNamespace.Xmlns + "manifest", Manifest.NamespaceName),
            new XAttribute(Manifest + "version", "1.3"));

        manifest.Add(FileEntry("/", MimeType));

        // Each embedded chart sub-document needs its directory declared with the chart media type
        // (the inner content.xml is added by the per-part loop below as text/xml).
        foreach (string dir in chartObjectDirs ?? [])
        {
            manifest.Add(FileEntry($"{dir}/", ChartMediaType));
        }

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
