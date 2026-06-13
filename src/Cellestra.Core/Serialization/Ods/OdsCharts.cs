using System.Text;
using System.Xml;
using System.Xml.Linq;
using Cellestra.Charts;
using Cellestra.Charts.Ods;
using Cellestra.Core.Model;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>
/// The spreadsheet-side of chart-sheet serialization: embeds each chart sheet's chart as an ODF
/// embedded object (a <c>draw:frame</c>/<c>draw:object</c> in the sheet's table that points at an
/// <c>Object N/</c> sub-document), and reads it back. The chart object's own <c>content.xml</c> is
/// built and parsed by <see cref="ChartDocument"/> in the chart library; this class only wires it
/// into the <c>.ods</c> package and the spreadsheet's <c>content.xml</c>.
/// </summary>
public static class OdsCharts
{
    /// <summary>
    /// Write each chart sheet's chart object into <paramref name="package"/> (assigning a stable
    /// <c>Object N</c> directory the first time), and return those directory names so the manifest
    /// can register them with the chart media type.
    /// </summary>
    public static IReadOnlyList<string> WriteObjects(Workbook workbook, OdsPackage package)
    {
        var dirs = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sheet in workbook.Worksheets)
        {
            if (sheet.Chart is { ObjectName.Length: > 0 } existing)
            {
                used.Add(existing.ObjectName);
            }
        }

        int counter = 1;
        foreach (var sheet in workbook.Worksheets)
        {
            if (sheet.Chart is not { } chart)
            {
                continue;
            }

            if (string.IsNullOrEmpty(chart.ObjectName))
            {
                string name;
                do
                {
                    name = $"Object {counter++}";
                }
                while (!used.Add(name) || package.Contains($"{name}/content.xml"));

                chart.ObjectName = name;
            }

            package.SetText($"{chart.ObjectName}/content.xml", Serialize(ChartDocument.BuildContent(chart)));
            dirs.Add(chart.ObjectName);
        }

        return dirs;
    }

    /// <summary>
    /// Embed the chart's <c>draw:frame</c> into a chart sheet's table element. Assumes
    /// <see cref="WriteObjects"/> has already assigned the chart's <see cref="ChartDefinition.ObjectName"/>.
    /// </summary>
    public static void InjectFrame(XElement table, Worksheet sheet)
    {
        if (sheet.Chart is not { ObjectName.Length: > 0 } chart)
        {
            return;
        }

        // A chart sheet has no cells, so the row writer emits a single empty row + cell; host the
        // frame there (creating the row/cell if a future change ever leaves the table bare).
        var firstRow = table.Elements(Table + "table-row").FirstOrDefault();
        if (firstRow is null)
        {
            firstRow = new XElement(Table + "table-row", new XElement(Table + "table-cell"));
            table.Add(firstRow);
        }

        var firstCell = firstRow.Elements(Table + "table-cell").FirstOrDefault();
        if (firstCell is null)
        {
            firstCell = new XElement(Table + "table-cell");
            firstRow.AddFirst(firstCell);
        }

        var frame = new XElement(Draw + "frame",
            new XAttribute(Draw + "name", $"{sheet.Name} Chart"),
            new XAttribute(Svg + "width", Cm(ChartDocument.DefaultWidthCm)),
            new XAttribute(Svg + "height", Cm(ChartDocument.DefaultHeightCm)),
            new XAttribute(Svg + "x", "0cm"),
            new XAttribute(Svg + "y", "0cm"),
            new XElement(Draw + "object",
                new XAttribute(Xlink + "href", "./" + chart.ObjectName),
                new XAttribute(Xlink + "type", "simple"),
                new XAttribute(Xlink + "show", "embed"),
                new XAttribute(Xlink + "actuate", "onLoad")));

        firstCell.Add(frame);
    }

    /// <summary>
    /// If <paramref name="table"/> embeds a chart object, parse it from <paramref name="package"/>
    /// and attach it to <paramref name="sheet"/> as a chart sheet. Silently does nothing when there
    /// is no embedded chart, or the referenced object isn't a parseable chart.
    /// </summary>
    public static void ReadChart(XElement table, OdsPackage? package, Worksheet sheet)
    {
        if (package is null)
        {
            return;
        }

        var reference = table.Descendants(Draw + "object")
            .Select(o => o.Attribute(Xlink + "href")?.Value)
            .FirstOrDefault(href => !string.IsNullOrEmpty(href));
        if (reference is null)
        {
            return;
        }

        // "./Object 1" or "Object 1/" → "Object 1".
        string dir = reference.TrimStart('.').Trim('/');
        string part = $"{dir}/content.xml";
        string? xml = package.GetText(part);
        if (xml is null)
        {
            return;
        }

        var chart = ChartDocument.Parse(xml);
        if (chart is null)
        {
            return;
        }

        chart.ObjectName = dir;
        sheet.Chart = chart;
    }

    private static string Cm(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "cm";

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
