using System.Xml.Linq;
using Cellestra.Core.Serialization.Ods;
using Xunit;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Tests.Serialization;

public class UnknownContentPreservationTests
{
    private static readonly byte[] ExtraPartBytes = [1, 2, 3, 4, 5];

    /// <summary>Hand-built .ods with an unmodeled spreadsheet child and an extra binary part.</summary>
    private static byte[] BuildPackageWithUnknownContent()
    {
        var content = new XElement(Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", Table.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", Text.NamespaceName),
            new XElement(Office + "body",
                new XElement(Office + "spreadsheet",
                    new XElement(Table + "table",
                        new XAttribute(Table + "name", "Sheet1"),
                        new XElement(Table + "table-row",
                            new XElement(Table + "table-cell",
                                new XAttribute(Office + "value-type", "float"),
                                new XAttribute(Office + "value", "5"),
                                new XElement(Text + "p", "5")))),
                    // An element Cellestra does not model (a pivot table) — must survive.
                    new XElement(Table + "data-pilot-tables",
                        new XElement(Table + "data-pilot-table", new XAttribute(Table + "name", "PT"))))));

        var package = new OdsPackage();
        package.SetText(MimetypePart, MimeType);
        package.SetText(ContentPart, new XDocument(content).ToString());
        package.SetBytes("Pictures/logo.bin", ExtraPartBytes);

        using var memory = new MemoryStream();
        package.Write(memory);
        return memory.ToArray();
    }

    [Fact]
    public void Unknown_spreadsheet_child_and_extra_part_survive_a_round_trip()
    {
        // Open the crafted file.
        using var input = new MemoryStream(BuildPackageWithUnknownContent());
        var (workbook, package) = OdsReader.ReadWithPackage(input);

        Assert.Equal(5, workbook.FindSheet("Sheet1")!.GetValue(0, 0).AsNumber);
        Assert.Contains(package.ForeignSpreadsheetChildren, f => f.Xml.Contains("data-pilot-tables", StringComparison.Ordinal));
        Assert.True(package.Contains("Pictures/logo.bin"));

        // Save it back through the model.
        using var output = new MemoryStream();
        OdsWriter.Write(workbook, output, package);

        // Re-open and confirm nothing was dropped.
        using var reopen = new MemoryStream(output.ToArray());
        var (workbook2, package2) = OdsReader.ReadWithPackage(reopen);

        Assert.Equal(5, workbook2.FindSheet("Sheet1")!.GetValue(0, 0).AsNumber);
        Assert.Contains(package2.ForeignSpreadsheetChildren, f => f.Xml.Contains("data-pilot-tables", StringComparison.Ordinal));
        Assert.True(package2.Contains("Pictures/logo.bin"));
        Assert.Equal(ExtraPartBytes, package2.GetBytes("Pictures/logo.bin"));
    }
}
