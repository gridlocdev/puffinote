using System.Text;
using System.Xml;
using System.Xml.Linq;
using Cellestra.Core.Model;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>
/// Writes a <see cref="Workbook"/> to a <c>.ods</c> stream. Regenerates content.xml,
/// settings.xml, and the manifest from the model; preserves the source package's styles.xml,
/// meta.xml, and all unmodeled parts and <c>office:spreadsheet</c> children verbatim (PRD §8.1).
/// </summary>
public static class OdsWriter
{
    public static void Write(Workbook workbook, Stream stream, OdsPackage? source = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var package = source ?? new OdsPackage();

        package.SetText(OdsXmlNames.MimetypePart, OdsXmlNames.MimeType);
        package.SetText(OdsXmlNames.ContentPart, Serialize(OdsContentXml.Write(workbook, package.ForeignSpreadsheetChildren)));
        package.SetText(OdsXmlNames.SettingsPart, Serialize(OdsSupportXml.BuildSettings(workbook)));

        if (!package.Contains(OdsXmlNames.StylesPart))
        {
            package.SetText(OdsXmlNames.StylesPart, Serialize(OdsSupportXml.BuildStyles()));
        }

        if (!package.Contains(OdsXmlNames.MetaPart))
        {
            package.SetText(OdsXmlNames.MetaPart, Serialize(OdsSupportXml.BuildMeta()));
        }

        // Manifest lists every part (it is regenerated to match the final contents).
        var parts = package.PartNames.Where(p => p != OdsXmlNames.ManifestPart).ToList();
        package.SetText(OdsXmlNames.ManifestPart, Serialize(OdsSupportXml.BuildManifest(parts)));

        package.Write(stream);
    }

    public static byte[] WriteToBytes(Workbook workbook, OdsPackage? source = null)
    {
        using var memory = new MemoryStream();
        Write(workbook, memory, source);
        return memory.ToArray();
    }

    private static string Serialize(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false,
        };

        using var memory = new MemoryStream();
        using (var writer = XmlWriter.Create(memory, settings))
        {
            document.Save(writer);
        }

        return new UTF8Encoding(false).GetString(memory.ToArray());
    }
}
