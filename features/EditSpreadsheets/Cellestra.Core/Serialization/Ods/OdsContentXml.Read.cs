using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods.Mapping;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods;

public static partial class OdsContentXml
{
    // ---- Read -------------------------------------------------------------

    public static void Read(XDocument document, Workbook workbook, List<ForeignXml> foreign, OdsPackage? package = null)
    {
        var root = document.Root!;
        var styleLookup = ReadAutomaticStyles(root, workbook.Styles, out var columnWidths, out var rowHeights, out var styleNumberFormats);

        var spreadsheet = root.Element(Office + "body")?.Element(Office + "spreadsheet");
        if (spreadsheet is null)
        {
            return;
        }

        // Parse the validation rule definitions up front; cells reference them by name, and we
        // reconstruct each rule's target range from the cells that point at it (collected below).
        var validationRules = ReadContentValidationRules(spreadsheet);
        var validationCells = new Dictionary<(string Sheet, string Name), List<(int Row, int Column)>>();

        bool seenTable = false;
        foreach (var child in spreadsheet.Elements())
        {
            if (child.Name == Table + "table")
            {
                seenTable = true;
                ReadTable(child, workbook, styleLookup, columnWidths, rowHeights, styleNumberFormats, validationCells, package);
            }
            else if (child.Name == Table + "named-expressions")
            {
                ReadNamedExpressions(child, workbook);
            }
            else if (child.Name == Table + DatabaseRanges)
            {
                ReadDatabaseRanges(child, workbook);
            }
            else if (child.Name == Table + ContentValidations)
            {
                // Modeled; the rules were parsed above. Do not preserve as foreign.
            }
            else
            {
                foreign.Add(new ForeignXml(!seenTable, child.ToString(SaveOptions.DisableFormatting)));
            }
        }

        AssembleValidations(workbook, validationRules, validationCells);
    }

    private static Dictionary<string, int> ReadAutomaticStyles(
        XElement root,
        StylePool pool,
        out Dictionary<string, double> columnWidths,
        out Dictionary<string, double> rowHeights,
        out Dictionary<string, string> dummy)
    {
        columnWidths = new Dictionary<string, double>(StringComparer.Ordinal);
        rowHeights = new Dictionary<string, double>(StringComparer.Ordinal);
        dummy = new Dictionary<string, string>(StringComparer.Ordinal);
        var styleIds = new Dictionary<string, int>(StringComparer.Ordinal);

        var autoStyles = root.Element(Office + "automatic-styles");
        if (autoStyles is null)
        {
            return styleIds;
        }

        var dataStyles = new Dictionary<string, NumberFormat>(StringComparer.Ordinal);
        foreach (var element in autoStyles.Elements())
        {
            if (element.Name.Namespace == Number)
            {
                string? name = element.Attribute(Style + "name")?.Value;
                if (name is not null)
                {
                    dataStyles[name] = new NumberFormat(OdsDataStyles.ToCode(element));
                }
            }
        }

        foreach (var element in autoStyles.Elements(Style + "style"))
        {
            string? name = element.Attribute(Style + "name")?.Value;
            string? family = element.Attribute(Style + "family")?.Value;
            if (name is null)
            {
                continue;
            }

            switch (family)
            {
                case "table-cell":
                    var cellStyle = OdsCellStyles.Parse(element, code =>
                        dataStyles.TryGetValue(code, out var nf) ? nf : NumberFormat.General);
                    styleIds[name] = pool.Intern(cellStyle);
                    break;
                case "table-column":
                    var cw = element.Element(Style + "table-column-properties")?.Attribute(Style + "column-width")?.Value;
                    if (ParseCm(cw) is { } width)
                    {
                        columnWidths[name] = CmToPx(width);
                    }

                    break;
                case "table-row":
                    var rh = element.Element(Style + "table-row-properties")?.Attribute(Style + "row-height")?.Value;
                    if (ParseCm(rh) is { } height)
                    {
                        rowHeights[name] = CmToPx(height);
                    }

                    break;
            }
        }

        return styleIds;
    }

    private static void ReadTable(
        XElement table,
        Workbook workbook,
        Dictionary<string, int> styleLookup,
        Dictionary<string, double> columnWidths,
        Dictionary<string, double> rowHeights,
        Dictionary<string, string> _,
        Dictionary<(string Sheet, string Name), List<(int Row, int Column)>> validationCells,
        OdsPackage? package)
    {
        string name = table.Attribute(Table + "name")?.Value ?? workbook.AddSheet().Name;
        var sheet = workbook.FindSheet(name) ?? workbook.AddSheetForLoad(name);

        ReadColumns(table, sheet, columnWidths, styleLookup);
        ReadRows(table, sheet, styleLookup, rowHeights, validationCells);

        // If the table embeds a chart object, attach it so the sheet opens as a chart sheet.
        OdsCharts.ReadChart(table, package, sheet);
    }

    private static void ReadColumns(XElement table, Worksheet sheet, Dictionary<string, double> columnWidths, Dictionary<string, int> styleLookup)
    {
        int column = 0;
        foreach (var element in table.Elements(Table + "table-column"))
        {
            int repeat = ReadRepeat(element, Table + "number-columns-repeated");
            string? styleName = element.Attribute(Table + "style-name")?.Value;
            bool hidden = element.Attribute(Table + "visibility")?.Value is "collapse" or "filter";
            double? width = styleName is not null && columnWidths.TryGetValue(styleName, out var w) ? w : null;

            // The column's default cell style — how every writer, ours included, stores "this whole
            // column is formatted like so" without touching a single cell.
            string? defaultCellStyle = element.Attribute(Table + "default-cell-style-name")?.Value;
            int? defaultStyleId = defaultCellStyle is not null && styleLookup.TryGetValue(defaultCellStyle, out int id)
                ? id
                : null;

            for (int i = 0; i < repeat; i++, column++)
            {
                if (width is null && !hidden && defaultStyleId is null)
                {
                    continue;
                }

                var model = sheet.GetOrCreateColumn(column);
                if (width is not null)
                {
                    model.Width = width.Value;
                }

                if (defaultStyleId is not null)
                {
                    model.StyleId = defaultStyleId.Value;
                }

                model.Hidden = hidden;
            }
        }
    }

    private static void ReadRows(XElement table, Worksheet sheet, Dictionary<string, int> styleLookup, Dictionary<string, double> rowHeights, Dictionary<(string Sheet, string Name), List<(int Row, int Column)>> validationCells)
    {
        int row = 0;
        foreach (var element in table.Elements(Table + "table-row"))
        {
            int repeat = ReadRepeat(element, Table + "number-rows-repeated");
            string? styleName = element.Attribute(Table + "style-name")?.Value;
            bool hidden = element.Attribute(Table + "visibility")?.Value is "collapse" or "filter";
            double? height = styleName is not null && rowHeights.TryGetValue(styleName, out var h) ? h : null;

            for (int i = 0; i < repeat; i++, row++)
            {
                if (height is not null || hidden)
                {
                    var model = sheet.GetOrCreateRow(row);
                    if (height is not null)
                    {
                        model.Height = height.Value;
                    }

                    model.Hidden = hidden;
                }

                ReadCells(element, sheet, row, styleLookup, validationCells);
            }
        }
    }

    private static void ReadCells(XElement rowElement, Worksheet sheet, int row, Dictionary<string, int> styleLookup, Dictionary<(string Sheet, string Name), List<(int Row, int Column)>> validationCells)
    {
        int column = 0;
        foreach (var element in rowElement.Elements())
        {
            bool covered = element.Name == Table + "covered-table-cell";
            bool isCell = covered || element.Name == Table + "table-cell";
            if (!isCell)
            {
                continue;
            }

            int repeat = ReadRepeat(element, Table + "number-columns-repeated");
            for (int i = 0; i < repeat; i++, column++)
            {
                if (covered)
                {
                    continue;
                }

                ReadCell(element, sheet, row, column, styleLookup, validationCells);
            }
        }
    }

    private static void ReadCell(XElement element, Worksheet sheet, int row, int column, Dictionary<string, int> styleLookup, Dictionary<(string Sheet, string Name), List<(int Row, int Column)>> validationCells)
    {
        var value = ReadValue(element);
        string? formula = element.Attribute(Table + "formula")?.Value;
        string? styleName = element.Attribute(Table + "style-name")?.Value;
        var hyperlink = ReadHyperlink(element);

        string? validationName = element.Attribute(Table + ContentValidationName)?.Value;
        if (validationName is not null)
        {
            var key = (sheet.Name, validationName);
            (validationCells.TryGetValue(key, out var list) ? list : validationCells[key] = [])
                .Add((row, column));
        }

        int columnsSpanned = ReadSpan(element, Table + "number-columns-spanned");
        int rowsSpanned = ReadSpan(element, Table + "number-rows-spanned");

        bool hasContent = !value.IsEmpty || formula is not null || styleName is not null || hyperlink is not null;
        if (hasContent)
        {
            var cell = sheet.GetOrCreate(row, column);
            cell.Value = value;
            cell.Formula = formula is not null ? OpenFormulaTranslator.FromOpenFormula(formula) : null;
            cell.Hyperlink = hyperlink;
            if (styleName is not null && styleLookup.TryGetValue(styleName, out int styleId))
            {
                cell.StyleId = styleId;
            }
        }

        if (columnsSpanned > 1 || rowsSpanned > 1)
        {
            var range = new CellRange(
                new CellAddress(column, row),
                new CellAddress(column + columnsSpanned - 1, row + rowsSpanned - 1));
            sheet.AddMerge(new MergedRegion(range));
        }
    }

    private static CellValue ReadValue(XElement element)
    {
        string? type = element.Attribute(Office + "value-type")?.Value;
        switch (type)
        {
            case "float":
            case "percentage":
            case "currency":
                return ParseDouble(element.Attribute(Office + "value")?.Value, out double number)
                    ? CellValue.Number(number)
                    : CellValue.Empty;
            case "date":
                var dateText = element.Attribute(Office + "date-value")?.Value;
                return DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? CellValue.Number(Formulas.Evaluation.DateConversion.ToSerial(date))
                    : CellValue.Empty;
            case "time":
                var timeText = element.Attribute(Office + "time-value")?.Value;
                return TryParseDuration(timeText, out double timeSerial)
                    ? CellValue.Number(timeSerial)
                    : CellValue.Empty;
            case "boolean":
                return CellValue.Boolean(element.Attribute(Office + "boolean-value")?.Value == "true");
            case "string":
                return CellValue.Text(ReadText(element));
            default:
                return CellValue.Empty;
        }
    }

    private static string ReadText(XElement element)
    {
        var paragraphs = element.Elements(Text + "p").Select(CellParagraphText);
        return string.Join("\n", paragraphs);
    }

    /// <summary>
    /// The text of a single <c>text:p</c>. A plain paragraph's value is significant as-is (so leading/
    /// trailing spaces in a string cell survive). A paragraph with child spans — e.g. a hyperlink
    /// <c>text:a</c> — is mixed content: the meaningful text lives in the spans (and any non-blank text
    /// between them), while whitespace-only text nodes around them are the serializer's pretty-print
    /// indentation, not content, so they're dropped. Without this a hyperlinked cell reopened as
    /// "\n  value\n" instead of "value".
    /// </summary>
    private static string CellParagraphText(XElement paragraph)
    {
        if (!paragraph.HasElements)
        {
            return paragraph.Value;
        }

        return string.Concat(paragraph.Nodes().Select(node => node switch
        {
            XElement span => span.Value,
            XText text when text.Value.Trim().Length > 0 => text.Value,
            _ => string.Empty,
        }));
    }

    private static Hyperlink? ReadHyperlink(XElement element)
    {
        var anchor = element.Descendants(Text + "a").FirstOrDefault();
        var href = anchor?.Attribute(Xlink + "href")?.Value;
        return href is null ? null : new Hyperlink(href);
    }

    private static void ReadNamedExpressions(XElement container, Workbook workbook)
    {
        foreach (var named in container.Elements(Table + "named-range"))
        {
            string? name = named.Attribute(Table + "name")?.Value;
            string? address = named.Attribute(Table + "cell-range-address")?.Value;
            if (name is null || address is null)
            {
                continue;
            }

            string internalRef = OpenFormulaTranslator.FromOpenFormula("of:=[" + address.TrimStart('$') + "]");
            if (CellRange.TryParse(internalRef, out var range) && workbook.FindNamedRange(name) is null)
            {
                workbook.AddNamedRange(new NamedRange(name, range));
            }
        }
    }

    private static int ReadRepeat(XElement element, XName attribute) =>
        int.TryParse(element.Attribute(attribute)?.Value, out int n) && n > 0 ? n : 1;

    private static int ReadSpan(XElement element, XName attribute) =>
        int.TryParse(element.Attribute(attribute)?.Value, out int n) && n > 0 ? n : 1;

    private static bool ParseDouble(string? text, out double value)
    {
        switch (text)
        {
            case "INF":
                value = double.PositiveInfinity;
                return true;
            case "-INF":
                value = double.NegativeInfinity;
                return true;
            case "NaN":
                value = double.NaN;
                return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Parse an ODF <c>office:time-value</c> ISO-8601 duration into a day serial.</summary>
    private static bool TryParseDuration(string? text, out double days)
    {
        days = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            days = System.Xml.XmlConvert.ToTimeSpan(text).TotalDays;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static double? ParseCm(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.EndsWith("cm", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cm))
        {
            return cm;
        }

        if (value.EndsWith("in", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double inches))
        {
            return inches * CmPerInch;
        }

        return null;
    }
}
