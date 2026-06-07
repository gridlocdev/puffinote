using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods.Mapping;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>
/// Builds and parses <c>content.xml</c> — the cell grid, automatic styles, merges, column
/// widths/row heights, hidden lines, and named ranges. Formulas go through
/// <see cref="OpenFormulaTranslator"/>. Unmodeled <c>office:spreadsheet</c> children are
/// captured/re-emitted so nothing is silently dropped (PRD §8.1).
/// </summary>
public static class OdsContentXml
{
    private const double PxPerInch = 96.0;
    private const double CmPerInch = 2.54;

    // ---- Write ------------------------------------------------------------

    public static XDocument Write(Workbook workbook, IReadOnlyList<ForeignXml> foreign)
    {
        var autoStyles = new XElement(Office + "automatic-styles");
        var registry = new StyleRegistry(workbook.Styles, autoStyles);

        var spreadsheet = new XElement(Office + "spreadsheet");

        foreach (var child in foreign.Where(f => f.BeforeTables))
        {
            spreadsheet.Add(XElement.Parse(child.Xml));
        }

        foreach (var sheet in workbook.Worksheets)
        {
            spreadsheet.Add(WriteTable(sheet, registry));
        }

        AddNamedExpressions(spreadsheet, workbook);

        foreach (var child in foreign.Where(f => !f.BeforeTables))
        {
            spreadsheet.Add(XElement.Parse(child.Xml));
        }

        var root = new XElement(Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", Table.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", Fo.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "number", Number.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "svg", Svg.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            autoStyles,
            new XElement(Office + "body", spreadsheet));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement WriteTable(Worksheet sheet, StyleRegistry registry)
    {
        var table = new XElement(Table + "table", new XAttribute(Table + "name", sheet.Name));
        var used = sheet.GetUsedRange();
        int lastColumn = used?.Right ?? 0;
        int lastRow = used?.Bottom ?? 0;

        WriteColumns(table, sheet, lastColumn, registry);
        WriteRows(table, sheet, lastRow, lastColumn, registry);
        return table;
    }

    private static void WriteColumns(XElement table, Worksheet sheet, int lastColumn, StyleRegistry registry)
    {
        // Emit one element per column up to the used width, coalescing identical neighbors.
        int column = 0;
        while (column <= lastColumn)
        {
            var (styleName, hidden) = ColumnSignature(sheet, column, registry);
            int repeat = 1;
            while (column + repeat <= lastColumn && ColumnSignature(sheet, column + repeat, registry) == (styleName, hidden))
            {
                repeat++;
            }

            var element = new XElement(Table + "table-column");
            if (styleName is not null)
            {
                element.Add(new XAttribute(Table + "style-name", styleName));
            }

            if (repeat > 1)
            {
                element.Add(new XAttribute(Table + "number-columns-repeated", repeat));
            }

            if (hidden)
            {
                element.Add(new XAttribute(Table + "visibility", "collapse"));
            }

            table.Add(element);
            column += repeat;
        }
    }

    private static (string? Style, bool Hidden) ColumnSignature(Worksheet sheet, int column, StyleRegistry registry)
    {
        var model = sheet.FindColumn(column);
        if (model is null)
        {
            return (null, false);
        }

        string? style = model.Width != Defaults.ColumnWidth ? registry.ColumnStyle(model.Width) : null;
        return (style, model.Hidden);
    }

    private static void WriteRows(XElement table, Worksheet sheet, int lastRow, int lastColumn, StyleRegistry registry)
    {
        int row = 0;
        while (row <= lastRow)
        {
            if (RowIsEmpty(sheet, row, lastColumn))
            {
                int repeat = 1;
                while (row + repeat <= lastRow && RowIsEmpty(sheet, row + repeat, lastColumn))
                {
                    repeat++;
                }

                var emptyRow = new XElement(Table + "table-row", new XElement(Table + "table-cell"));
                if (repeat > 1)
                {
                    emptyRow.Add(new XAttribute(Table + "number-rows-repeated", repeat));
                }

                table.Add(emptyRow);
                row += repeat;
                continue;
            }

            table.Add(WriteRow(sheet, row, lastColumn, registry));
            row++;
        }
    }

    private static bool RowIsEmpty(Worksheet sheet, int row, int lastColumn)
    {
        var model = sheet.FindRow(row);
        if (model is { IsDefault: false })
        {
            return false;
        }

        for (int c = 0; c <= lastColumn; c++)
        {
            if (sheet.Find(row, c) is not null || sheet.FindMerge(row, c) is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static XElement WriteRow(Worksheet sheet, int row, int lastColumn, StyleRegistry registry)
    {
        var element = new XElement(Table + "table-row");
        var rowModel = sheet.FindRow(row);
        if (rowModel is { Height: var h } && h != Defaults.RowHeight)
        {
            element.Add(new XAttribute(Table + "style-name", registry.RowStyle(h)));
        }

        if (rowModel is { Hidden: true })
        {
            element.Add(new XAttribute(Table + "visibility", "collapse"));
        }

        int column = 0;
        while (column <= lastColumn)
        {
            var merge = sheet.FindMerge(row, column);
            if (merge is not null && merge.IsCovered(row, column))
            {
                element.Add(new XElement(Table + "covered-table-cell"));
                column++;
                continue;
            }

            var cell = sheet.Find(row, column);
            if (cell is null && merge is null)
            {
                // Coalesce a run of blank cells.
                int repeat = 1;
                while (column + repeat <= lastColumn &&
                       sheet.Find(row, column + repeat) is null &&
                       sheet.FindMerge(row, column + repeat) is null)
                {
                    repeat++;
                }

                var blank = new XElement(Table + "table-cell");
                if (repeat > 1)
                {
                    blank.Add(new XAttribute(Table + "number-columns-repeated", repeat));
                }

                element.Add(blank);
                column += repeat;
                continue;
            }

            element.Add(WriteCell(cell, merge, row, column, registry));
            column++;
        }

        return element;
    }

    private static XElement WriteCell(Cell? cell, MergedRegion? merge, int row, int column, StyleRegistry registry)
    {
        var element = new XElement(Table + "table-cell");

        if (cell is not null && cell.StyleId != StylePool.DefaultId)
        {
            element.Add(new XAttribute(Table + "style-name", registry.CellStyle(cell.StyleId)));
        }

        if (merge is not null && merge.Range.Top == row && merge.Range.Left == column)
        {
            if (merge.Range.ColumnCount > 1)
            {
                element.Add(new XAttribute(Table + "number-columns-spanned", merge.Range.ColumnCount));
            }

            if (merge.Range.RowCount > 1)
            {
                element.Add(new XAttribute(Table + "number-rows-spanned", merge.Range.RowCount));
            }
        }

        if (cell is not null)
        {
            WriteCellContent(element, cell, registry);
        }

        return element;
    }

    private static void WriteCellContent(XElement element, Cell cell, StyleRegistry registry)
    {
        if (cell.IsFormula)
        {
            element.Add(new XAttribute(Table + "formula", OpenFormulaTranslator.ToOpenFormula(cell.Formula!)));
        }

        var value = cell.Value;
        bool isDate = registry.Styles.Get(cell.StyleId).NumberFormat.Kind is
            NumberFormatKind.Date or NumberFormatKind.DateTime or NumberFormatKind.Time;

        switch (value.Type)
        {
            case CellValueType.Number when isDate:
                element.Add(new XAttribute(Office + "value-type", "date"));
                element.Add(new XAttribute(Office + "date-value",
                    Formulas.Evaluation.DateConversion.DateFromSerial(value.AsNumber).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                element.Add(new XElement(Text + "p", DisplayText(cell, registry)));
                break;
            case CellValueType.Number:
                element.Add(new XAttribute(Office + "value-type", "float"));
                element.Add(new XAttribute(Office + "value", value.AsNumber.ToString("R", CultureInfo.InvariantCulture)));
                element.Add(new XElement(Text + "p", DisplayText(cell, registry)));
                break;
            case CellValueType.Boolean:
                element.Add(new XAttribute(Office + "value-type", "boolean"));
                element.Add(new XAttribute(Office + "boolean-value", value.AsBoolean ? "true" : "false"));
                element.Add(new XElement(Text + "p", value.AsBoolean ? "TRUE" : "FALSE"));
                break;
            case CellValueType.Text:
                element.Add(new XAttribute(Office + "value-type", "string"));
                element.Add(new XElement(Text + "p", value.AsText));
                break;
            case CellValueType.Error:
                element.Add(new XAttribute(Office + "value-type", "string"));
                element.Add(new XElement(Text + "p", value.AsError.Token));
                break;
        }

        if (cell.Hyperlink is not null && element.Element(Text + "p") is { } paragraph)
        {
            var anchor = new XElement(Text + "a",
                new XAttribute(Xlink + "href", cell.Hyperlink.Target),
                paragraph.Value);
            paragraph.Value = string.Empty;
            paragraph.Add(anchor);
        }
    }

    private static string DisplayText(Cell cell, StyleRegistry registry) =>
        NumberFormatter.Format(cell.Value, registry.Styles.Get(cell.StyleId).NumberFormat, CultureInfo.InvariantCulture);

    private static void AddNamedExpressions(XElement spreadsheet, Workbook workbook)
    {
        var global = workbook.NamedRanges.Where(n => n.IsGlobal).ToList();
        if (global.Count == 0)
        {
            return;
        }

        string fallbackSheet = workbook.Worksheets.Count > 0 ? workbook.Worksheets[0].Name : Defaults.FirstSheetName;
        var container = new XElement(Table + "named-expressions");
        foreach (var named in global)
        {
            container.Add(new XElement(Table + "named-range",
                new XAttribute(Table + "name", named.Name),
                new XAttribute(Table + "cell-range-address", OdfRangeAddress(named.Target, fallbackSheet)),
                new XAttribute(Table + "base-cell-address", OdfCellAddress(named.Target.TopLeft, fallbackSheet))));
        }

        spreadsheet.Add(container);
    }

    private static string OdfRangeAddress(CellRange range, string fallbackSheet)
    {
        string sheet = range.Sheet ?? fallbackSheet;
        string start = OdfCellAddress(new CellAddress(range.Left, range.Top, sheet: sheet), fallbackSheet);
        if (range.RowCount == 1 && range.ColumnCount == 1)
        {
            return start;
        }

        string end = OdfCellAddress(new CellAddress(range.Right, range.Bottom, sheet: sheet), fallbackSheet);
        return $"{start}:{end}";
    }

    private static string OdfCellAddress(CellAddress address, string fallbackSheet)
    {
        string sheet = address.Sheet ?? fallbackSheet;
        return $"${sheet}.${address.ColumnLabel}${address.RowLabel}";
    }

    private static double PxToCm(double px) => px / PxPerInch * CmPerInch;

    private static double CmToPx(double cm) => cm / CmPerInch * PxPerInch;

    // ---- Style registry (write side) -------------------------------------

    private sealed class StyleRegistry
    {
        private readonly XElement _autoStyles;
        private readonly Dictionary<int, string> _cellStyleNames = new();
        private readonly Dictionary<string, string> _dataStyleNames = new(StringComparer.Ordinal);
        private readonly Dictionary<double, string> _columnStyleNames = new();
        private readonly Dictionary<double, string> _rowStyleNames = new();
        private int _next;

        public StyleRegistry(StylePool styles, XElement autoStyles)
        {
            Styles = styles;
            _autoStyles = autoStyles;
        }

        public StylePool Styles { get; }

        public string CellStyle(int styleId)
        {
            if (_cellStyleNames.TryGetValue(styleId, out var existing))
            {
                return existing;
            }

            var style = Styles.Get(styleId);
            string? dataStyleName = null;
            if (!style.NumberFormat.IsGeneral)
            {
                dataStyleName = DataStyle(style.NumberFormat);
            }

            string name = $"ce{_next++}";
            _autoStyles.Add(OdsCellStyles.Build(name, style, dataStyleName));
            _cellStyleNames[styleId] = name;
            return name;
        }

        private string DataStyle(NumberFormat format)
        {
            if (_dataStyleNames.TryGetValue(format.Code, out var existing))
            {
                return existing;
            }

            string name = $"N{_next++}";
            var element = OdsDataStyles.Build(name, format);
            if (element is null)
            {
                return name; // General; should not happen since caller checks
            }

            _autoStyles.Add(element);
            _dataStyleNames[format.Code] = name;
            return name;
        }

        public string ColumnStyle(double width)
        {
            if (_columnStyleNames.TryGetValue(width, out var existing))
            {
                return existing;
            }

            string name = $"co{_next++}";
            _autoStyles.Add(new XElement(Style + "style",
                new XAttribute(Style + "name", name),
                new XAttribute(Style + "family", "table-column"),
                new XElement(Style + "table-column-properties",
                    new XAttribute(Style + "column-width", FormatCm(PxToCm(width))))));
            _columnStyleNames[width] = name;
            return name;
        }

        public string RowStyle(double height)
        {
            if (_rowStyleNames.TryGetValue(height, out var existing))
            {
                return existing;
            }

            string name = $"ro{_next++}";
            _autoStyles.Add(new XElement(Style + "style",
                new XAttribute(Style + "name", name),
                new XAttribute(Style + "family", "table-row"),
                new XElement(Style + "table-row-properties",
                    new XAttribute(Style + "row-height", FormatCm(PxToCm(height))))));
            _rowStyleNames[height] = name;
            return name;
        }

        private static string FormatCm(double cm) => cm.ToString("0.###", CultureInfo.InvariantCulture) + "cm";
    }

    // ---- Read -------------------------------------------------------------

    public static void Read(XDocument document, Workbook workbook, List<ForeignXml> foreign)
    {
        var root = document.Root!;
        var styleLookup = ReadAutomaticStyles(root, workbook.Styles, out var columnWidths, out var rowHeights, out var styleNumberFormats);

        var spreadsheet = root.Element(Office + "body")?.Element(Office + "spreadsheet");
        if (spreadsheet is null)
        {
            return;
        }

        bool seenTable = false;
        foreach (var child in spreadsheet.Elements())
        {
            if (child.Name == Table + "table")
            {
                seenTable = true;
                ReadTable(child, workbook, styleLookup, columnWidths, rowHeights, styleNumberFormats);
            }
            else if (child.Name == Table + "named-expressions")
            {
                ReadNamedExpressions(child, workbook);
            }
            else
            {
                foreign.Add(new ForeignXml(!seenTable, child.ToString(SaveOptions.DisableFormatting)));
            }
        }
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
        Dictionary<string, string> _)
    {
        string name = table.Attribute(Table + "name")?.Value ?? workbook.AddSheet().Name;
        var sheet = workbook.FindSheet(name) ?? workbook.AddSheet(name);

        ReadColumns(table, sheet, columnWidths);
        ReadRows(table, sheet, styleLookup, rowHeights);
    }

    private static void ReadColumns(XElement table, Worksheet sheet, Dictionary<string, double> columnWidths)
    {
        int column = 0;
        foreach (var element in table.Elements(Table + "table-column"))
        {
            int repeat = ReadRepeat(element, Table + "number-columns-repeated");
            string? styleName = element.Attribute(Table + "style-name")?.Value;
            bool hidden = element.Attribute(Table + "visibility")?.Value is "collapse" or "filter";
            double? width = styleName is not null && columnWidths.TryGetValue(styleName, out var w) ? w : null;

            for (int i = 0; i < repeat; i++, column++)
            {
                if (width is null && !hidden)
                {
                    continue;
                }

                var model = sheet.GetOrCreateColumn(column);
                if (width is not null)
                {
                    model.Width = width.Value;
                }

                model.Hidden = hidden;
            }
        }
    }

    private static void ReadRows(XElement table, Worksheet sheet, Dictionary<string, int> styleLookup, Dictionary<string, double> rowHeights)
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

                ReadCells(element, sheet, row, styleLookup);
            }
        }
    }

    private static void ReadCells(XElement rowElement, Worksheet sheet, int row, Dictionary<string, int> styleLookup)
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

                ReadCell(element, sheet, row, column, styleLookup);
            }
        }
    }

    private static void ReadCell(XElement element, Worksheet sheet, int row, int column, Dictionary<string, int> styleLookup)
    {
        var value = ReadValue(element);
        string? formula = element.Attribute(Table + "formula")?.Value;
        string? styleName = element.Attribute(Table + "style-name")?.Value;
        var hyperlink = ReadHyperlink(element);

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
        var paragraphs = element.Elements(Text + "p").Select(p => p.Value);
        return string.Join("\n", paragraphs);
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

    private static bool ParseDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

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
