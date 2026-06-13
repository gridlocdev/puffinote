using System.Xml.Linq;
using Cellestra.Core.Model;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>
/// Reads a <c>.ods</c> stream into a <see cref="Workbook"/>. Returns the backing
/// <see cref="OdsPackage"/> too so a later save can preserve unmodeled parts and content
/// verbatim (PRD §8.1). Cached cell values are loaded as-is for instant display (PRD §8.6).
/// </summary>
public static class OdsReader
{
    public static (Workbook Workbook, OdsPackage Package) ReadWithPackage(Stream stream)
    {
        var package = OdsPackage.Read(stream);
        var workbook = FromPackage(package);
        return (workbook, package);
    }

    public static Workbook Read(Stream stream) => ReadWithPackage(stream).Workbook;

    public static Workbook FromPackage(OdsPackage package)
    {
        var workbook = new Workbook();

        string? contentXml = package.GetText(OdsXmlNames.ContentPart);
        if (contentXml is not null)
        {
            var document = XDocument.Parse(contentXml, LoadOptions.PreserveWhitespace);
            OdsContentXml.Read(document, workbook, package.ForeignSpreadsheetChildren, package);
        }

        // A valid spreadsheet always has at least one sheet.
        if (workbook.SheetCount == 0)
        {
            workbook.AddSheet(Defaults.FirstSheetName);
        }

        string? settingsXml = package.GetText(OdsXmlNames.SettingsPart);
        if (settingsXml is not null)
        {
            OdsSupportXml.ApplySettings(settingsXml, workbook);
        }

        return workbook;
    }
}
