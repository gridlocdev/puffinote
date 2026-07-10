using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods;

public static partial class OdsContentXml
{
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

        // Data validations are declared once, before the tables (ODF schema order), and referenced
        // by name from each validated cell. The map drives the per-cell content-validation-name.
        var validationNames = AddContentValidations(spreadsheet, workbook);

        foreach (var sheet in workbook.Worksheets)
        {
            spreadsheet.Add(WriteTable(sheet, registry, validationNames));
        }

        AddNamedExpressions(spreadsheet, workbook);
        AddDatabaseRanges(spreadsheet, workbook);

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
            new XAttribute(XNamespace.Xmlns + "draw", Draw.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", Xlink.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            autoStyles,
            new XElement(Office + "body", spreadsheet));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement WriteTable(Worksheet sheet, StyleRegistry registry, IReadOnlyDictionary<string, IReadOnlyDictionary<(int Row, int Column), string>> validationNames)
    {
        var table = new XElement(Table + "table", new XAttribute(Table + "name", sheet.Name));
        var used = sheet.GetUsedRange();
        int lastColumn = used?.Right ?? 0;
        int lastRow = used?.Bottom ?? 0;

        // A validated cell must be emitted (with its content-validation-name) even when it is blank
        // and lies past the used range, so extend the written bounds to cover every validation.
        var cellValidation = validationNames.TryGetValue(sheet.Name, out var map)
            ? map
            : EmptyValidationMap;
        foreach (var validation in sheet.Validations)
        {
            lastColumn = System.Math.Max(lastColumn, validation.Target.Right);
            lastRow = System.Math.Max(lastRow, validation.Target.Bottom);
        }

        WriteColumns(table, sheet, lastColumn, registry);
        WriteRows(table, sheet, lastRow, lastColumn, registry, cellValidation);

        // A chart sheet embeds its chart as an ODF draw frame in the table (the chart object's own
        // content.xml is written into the package by OdsCharts.WriteObjects, called first by OdsWriter).
        if (sheet.IsChartSheet)
        {
            OdsCharts.InjectFrame(table, sheet);
        }

        return table;
    }

    private static readonly IReadOnlyDictionary<(int Row, int Column), string> EmptyValidationMap =
        new Dictionary<(int, int), string>();

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

    private static void WriteRows(XElement table, Worksheet sheet, int lastRow, int lastColumn, StyleRegistry registry, IReadOnlyDictionary<(int Row, int Column), string> cellValidation)
    {
        int row = 0;
        while (row <= lastRow)
        {
            if (RowIsEmpty(sheet, row, lastColumn, cellValidation))
            {
                int repeat = 1;
                while (row + repeat <= lastRow && RowIsEmpty(sheet, row + repeat, lastColumn, cellValidation))
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

            table.Add(WriteRow(sheet, row, lastColumn, registry, cellValidation));
            row++;
        }
    }

    private static bool RowIsEmpty(Worksheet sheet, int row, int lastColumn, IReadOnlyDictionary<(int Row, int Column), string> cellValidation)
    {
        var model = sheet.FindRow(row);
        if (model is { IsDefault: false })
        {
            return false;
        }

        for (int c = 0; c <= lastColumn; c++)
        {
            if (sheet.Find(row, c) is not null || sheet.FindMerge(row, c) is not null || cellValidation.ContainsKey((row, c)))
            {
                return false;
            }
        }

        return true;
    }

    private static XElement WriteRow(Worksheet sheet, int row, int lastColumn, StyleRegistry registry, IReadOnlyDictionary<(int Row, int Column), string> cellValidation)
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
            if (cell is null && merge is null && !cellValidation.ContainsKey((row, column)))
            {
                // Coalesce a run of blank, unvalidated cells (a validated blank cell must be emitted
                // on its own so it can carry the content-validation-name attribute).
                int repeat = 1;
                while (column + repeat <= lastColumn &&
                       sheet.Find(row, column + repeat) is null &&
                       sheet.FindMerge(row, column + repeat) is null &&
                       !cellValidation.ContainsKey((row, column + repeat)))
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

            element.Add(WriteCell(cell, merge, row, column, registry, cellValidation));
            column++;
        }

        return element;
    }

    private static XElement WriteCell(Cell? cell, MergedRegion? merge, int row, int column, StyleRegistry registry, IReadOnlyDictionary<(int Row, int Column), string> cellValidation)
    {
        var element = new XElement(Table + "table-cell");

        if (cell is not null && cell.StyleId != StylePool.DefaultId)
        {
            element.Add(new XAttribute(Table + "style-name", registry.CellStyle(cell.StyleId)));
        }

        if (cellValidation.TryGetValue((row, column), out var validationName))
        {
            element.Add(new XAttribute(Table + ContentValidationName, validationName));
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
        var kind = registry.Styles.Get(cell.StyleId).NumberFormat.Kind;
        bool isTime = kind is NumberFormatKind.Time;
        bool isDate = kind is NumberFormatKind.Date or NumberFormatKind.DateTime;

        switch (value.Type)
        {
            case CellValueType.Number when isTime:
                // A time format stores the serial as an ISO-8601 duration so the whole value
                // (days *and* time-of-day fraction) survives, including elapsed times past 24h.
                element.Add(new XAttribute(Office + "value-type", "time"));
                element.Add(new XAttribute(Office + "time-value",
                    System.Xml.XmlConvert.ToString(TimeSpan.FromDays(value.AsNumber))));
                element.Add(new XElement(Text + "p", DisplayText(cell, registry)));
                break;
            case CellValueType.Number when isDate:
                element.Add(new XAttribute(Office + "value-type", "date"));
                element.Add(new XAttribute(Office + "date-value", DateValueText(value.AsNumber)));
                element.Add(new XElement(Text + "p", DisplayText(cell, registry)));
                break;
            case CellValueType.Number:
                element.Add(new XAttribute(Office + "value-type", "float"));
                element.Add(new XAttribute(Office + "value", FloatValueText(value.AsNumber)));
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

    /// <summary>
    /// Render a number as an ODF <c>office:value</c> (an <c>xsd:double</c>). The non-finite values
    /// spell as <c>INF</c>/<c>-INF</c>/<c>NaN</c>; .NET's "R" would emit "Infinity"/"-Infinity",
    /// which conformant readers (LibreOffice, Excel) reject as invalid.
    /// </summary>
    private static string FloatValueText(double number)
    {
        if (double.IsNaN(number))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(number))
        {
            return "INF";
        }

        if (double.IsNegativeInfinity(number))
        {
            return "-INF";
        }

        return number.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Render a date serial as an ODF <c>office:date-value</c>. Pure dates stay <c>yyyy-MM-dd</c>;
    /// values carrying a time-of-day fraction emit the full ISO-8601 timestamp so the time is not
    /// silently dropped on save. Out-of-range serials fall back to a plain date string.
    /// </summary>
    private static string DateValueText(double serial)
    {
        if (!Formulas.Evaluation.DateConversion.TryToDateTime(serial, dateOnly: false, out var when))
        {
            return Formulas.Evaluation.DateConversion.DateFromSerial(serial).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return when.TimeOfDay == TimeSpan.Zero
            ? when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : when.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

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
}
