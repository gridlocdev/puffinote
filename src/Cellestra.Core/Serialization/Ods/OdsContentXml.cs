using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods.Mapping;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;
using DataTable = Cellestra.Core.Data.Table;
using FilterSpec = Cellestra.Core.Data.FilterSpec;
using ColumnFilter = Cellestra.Core.Data.ColumnFilter;
using SortSpec = Cellestra.Core.Data.SortSpec;
using SortLevel = Cellestra.Core.Data.SortLevel;
using FilterEngine = Cellestra.Core.Data.FilterEngine;

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
        if (double.IsNaN(number)) return "NaN";
        if (double.IsPositiveInfinity(number)) return "INF";
        if (double.IsNegativeInfinity(number)) return "-INF";
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

    // ---- table:content-validation (data validation, PRD §8.13) -----------

    /// <summary>
    /// Emit a <c>table:content-validations</c> block (one <c>table:content-validation</c> per rule
    /// across all sheets) and return, per sheet, a map from each validated cell to the rule's name.
    /// The rule's condition encodes its kind (whole-number/decimal/date/text-length/list), operator,
    /// operands and list values in OpenFormula syntax; the error response maps to the
    /// <c>table:error-message</c>'s <c>message-type</c>. Returns an empty map when nothing is
    /// validated, so no block is written (PRD §8.13).
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<(int Row, int Column), string>> AddContentValidations(XElement spreadsheet, Workbook workbook)
    {
        var perSheet = new Dictionary<string, IReadOnlyDictionary<(int Row, int Column), string>>(StringComparer.Ordinal);
        var container = new XElement(Table + ContentValidations);
        int next = 0;

        foreach (var sheet in workbook.Worksheets)
        {
            if (sheet.Validations.Count == 0)
            {
                continue;
            }

            var cellMap = new Dictionary<(int Row, int Column), string>();
            foreach (var validation in sheet.Validations)
            {
                string name = $"val{next++}";
                container.Add(WriteContentValidation(name, validation, sheet.Name));
                foreach (var (row, column) in validation.Target.Positions())
                {
                    cellMap[(row, column)] = name; // last rule wins on overlap (rules should not overlap)
                }
            }

            perSheet[sheet.Name] = cellMap;
        }

        if (container.HasElements)
        {
            spreadsheet.Add(container);
        }

        return perSheet;
    }

    private static XElement WriteContentValidation(string name, Data.DataValidation validation, string sheetName)
    {
        var element = new XElement(Table + ContentValidation,
            new XAttribute(Table + "name", name),
            new XAttribute(Table + Condition, BuildCondition(validation, sheetName)),
            new XAttribute(Table + AllowEmptyCell, "true"),
            new XAttribute(Table + BaseCellAddress, OdfCellAddress(validation.Target.TopLeft, sheetName)));

        if (validation.Kind == Data.ValidationKind.List)
        {
            element.Add(new XAttribute(Table + DisplayList, "unsorted"));
        }

        if (validation.InputMessage is { Length: > 0 } input)
        {
            element.Add(new XElement(Table + HelpMessage,
                new XAttribute(Table + Display, "true"),
                new XElement(Text + "p", input)));
        }

        // The error response: Stop/Warning/Information map to the ODF message-type; Stop rejects.
        element.Add(new XElement(Table + ErrorMessage,
            new XAttribute(Table + Display, "true"),
            new XAttribute(Table + MessageType, MessageTypeOf(validation.Response)),
            validation.ErrorMessage is { Length: > 0 } err ? new XElement(Text + "p", err) : null));

        return element;
    }

    private static string MessageTypeOf(Data.ValidationResponse response) => response switch
    {
        Data.ValidationResponse.Warning => "warning",
        Data.ValidationResponse.Information => "information",
        _ => "stop",
    };

    private static Data.ValidationResponse ResponseOf(string? messageType) => messageType switch
    {
        "warning" => Data.ValidationResponse.Warning,
        "information" => Data.ValidationResponse.Information,
        _ => Data.ValidationResponse.Stop,
    };

    private static string BuildCondition(Data.DataValidation validation, string sheetName)
    {
        if (validation.Kind == Data.ValidationKind.List)
        {
            if (validation.ListSource is { } source)
            {
                return $"of:cell-content-is-in-list([{OdfRangeAddress(source.WithSheet(source.Sheet ?? sheetName), sheetName)}])";
            }

            string items = string.Join(";", (validation.ListValues ?? Array.Empty<string>())
                .Select(v => "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
            return $"of:cell-content-is-in-list({items})";
        }

        string kindPredicate = validation.Kind switch
        {
            Data.ValidationKind.WholeNumber => "of:cell-content-is-whole-number() and ",
            Data.ValidationKind.Decimal => "of:cell-content-is-decimal-number() and ",
            Data.ValidationKind.Date => "of:cell-content-is-date() and ",
            _ => string.Empty, // TextLength carries its kind in the content() call below
        };

        string content = validation.Kind == Data.ValidationKind.TextLength
            ? "of:cell-content-text-length()"
            : "of:cell-content()";

        string op1 = Num(validation.Operand1);
        string op2 = Num(validation.Operand2);

        string comparison = validation.Operator switch
        {
            Data.ValidationOperator.Between => $"of:cell-content-is-between({op1},{op2})",
            Data.ValidationOperator.NotBetween => $"of:cell-content-is-not-between({op1},{op2})",
            Data.ValidationOperator.Equal => $"{content}={op1}",
            Data.ValidationOperator.NotEqual => $"{content}!={op1}",
            Data.ValidationOperator.GreaterThan => $"{content}>{op1}",
            Data.ValidationOperator.LessThan => $"{content}<{op1}",
            Data.ValidationOperator.GreaterOrEqual => $"{content}>={op1}",
            Data.ValidationOperator.LessOrEqual => $"{content}<={op1}",
            _ => $"{content}={op1}",
        };

        // Text-length between uses the text-length variant of the between predicate.
        if (validation.Kind == Data.ValidationKind.TextLength)
        {
            comparison = validation.Operator switch
            {
                Data.ValidationOperator.Between => $"of:cell-content-text-length-is-between({op1},{op2})",
                Data.ValidationOperator.NotBetween => $"of:cell-content-text-length-is-not-between({op1},{op2})",
                _ => comparison,
            };
        }

        return kindPredicate + comparison;

        static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    // ---- table:content-validation (read side) ----------------------------

    /// <summary>A parsed validation rule sans its target range (the target comes from the cells).</summary>
    private sealed record ValidationRule(
        Data.ValidationKind Kind,
        Data.ValidationOperator Operator,
        double Operand1,
        double Operand2,
        IReadOnlyList<string>? ListValues,
        CellRange? ListSource,
        string? InputMessage,
        string? ErrorMessage,
        Data.ValidationResponse Response);

    private static Dictionary<string, ValidationRule> ReadContentValidationRules(XElement spreadsheet)
    {
        var rules = new Dictionary<string, ValidationRule>(StringComparer.Ordinal);
        var container = spreadsheet.Element(Table + ContentValidations);
        if (container is null)
        {
            return rules;
        }

        foreach (var element in container.Elements(Table + ContentValidation))
        {
            string? name = element.Attribute(Table + "name")?.Value;
            if (name is null)
            {
                continue;
            }

            string condition = element.Attribute(Table + Condition)?.Value ?? string.Empty;
            var errorElement = element.Element(Table + ErrorMessage);
            var helpElement = element.Element(Table + HelpMessage);

            var (kind, op, op1, op2, listValues, listSource) = ParseCondition(condition);

            rules[name] = new ValidationRule(
                kind, op, op1, op2, listValues, listSource,
                InputMessage: ParagraphText(helpElement),
                ErrorMessage: ParagraphText(errorElement),
                Response: ResponseOf(errorElement?.Attribute(Table + MessageType)?.Value));
        }

        return rules;
    }

    private static string? ParagraphText(XElement? messageElement)
    {
        var text = messageElement?.Elements(Text + "p").Select(p => p.Value);
        if (text is null)
        {
            return null;
        }

        string joined = string.Join("\n", text);
        return joined.Length > 0 ? joined : null;
    }

    private static (Data.ValidationKind Kind, Data.ValidationOperator Operator, double Op1, double Op2, IReadOnlyList<string>? ListValues, CellRange? ListSource) ParseCondition(string condition)
    {
        condition = condition.Trim();

        const string inList = "of:cell-content-is-in-list(";
        if (condition.StartsWith(inList, StringComparison.Ordinal) && condition.EndsWith(")", StringComparison.Ordinal))
        {
            string inner = condition[inList.Length..^1];
            if (inner.StartsWith('[') && inner.EndsWith(']'))
            {
                string internalRef = OpenFormulaTranslator.FromOpenFormula("of:=" + inner);
                CellRange? source = CellRange.TryParse(internalRef, out var range) ? range : null;
                return (Data.ValidationKind.List, Data.ValidationOperator.Between, 0, 0, null, source);
            }

            return (Data.ValidationKind.List, Data.ValidationOperator.Between, 0, 0, ParseQuotedList(inner), null);
        }

        // Otherwise a number/date/text-length constraint. The leading predicate (if any) gives the
        // kind; the comparison gives the operator and operands.
        var kind = Data.ValidationKind.Decimal;
        if (condition.Contains("cell-content-is-whole-number", StringComparison.Ordinal))
        {
            kind = Data.ValidationKind.WholeNumber;
        }
        else if (condition.Contains("cell-content-is-date", StringComparison.Ordinal))
        {
            kind = Data.ValidationKind.Date;
        }
        else if (condition.Contains("cell-content-text-length", StringComparison.Ordinal))
        {
            kind = Data.ValidationKind.TextLength;
        }

        // The comparison clause is whatever follows the last " and " (or the whole string).
        int andIndex = condition.LastIndexOf(" and ", StringComparison.Ordinal);
        string comparison = andIndex >= 0 ? condition[(andIndex + 5)..] : condition;

        var (op, op1, op2) = ParseComparison(comparison);
        return (kind, op, op1, op2, null, null);
    }

    private static (Data.ValidationOperator Operator, double Op1, double Op2) ParseComparison(string comparison)
    {
        comparison = comparison.Trim();

        foreach (var (token, op) in new[]
                 {
                     ("is-not-between(", Data.ValidationOperator.NotBetween),
                     ("is-between(", Data.ValidationOperator.Between),
                 })
        {
            int start = comparison.IndexOf(token, StringComparison.Ordinal);
            if (start >= 0)
            {
                int open = start + token.Length;
                int close = comparison.IndexOf(')', open);
                if (close > open)
                {
                    var parts = comparison[open..close].Split(',');
                    double a = parts.Length > 0 ? ParseInvariant(parts[0]) : 0;
                    double b = parts.Length > 1 ? ParseInvariant(parts[1]) : 0;
                    return (op, a, b);
                }
            }
        }

        // Relational: cell-content()OP value (or cell-content-text-length()OP value).
        int parens = comparison.IndexOf("()", StringComparison.Ordinal);
        string tail = parens >= 0 ? comparison[(parens + 2)..].Trim() : comparison;

        foreach (var (prefix, op) in new[]
                 {
                     (">=", Data.ValidationOperator.GreaterOrEqual),
                     ("<=", Data.ValidationOperator.LessOrEqual),
                     ("!=", Data.ValidationOperator.NotEqual),
                     ("<>", Data.ValidationOperator.NotEqual),
                     (">", Data.ValidationOperator.GreaterThan),
                     ("<", Data.ValidationOperator.LessThan),
                     ("=", Data.ValidationOperator.Equal),
                 })
        {
            if (tail.StartsWith(prefix, StringComparison.Ordinal))
            {
                return (op, ParseInvariant(tail[prefix.Length..]), 0);
            }
        }

        return (Data.ValidationOperator.Between, 0, 0);
    }

    private static IReadOnlyList<string> ParseQuotedList(string inner)
    {
        var values = new List<string>();
        int i = 0;
        while (i < inner.Length)
        {
            // Skip separators/whitespace between items.
            while (i < inner.Length && (inner[i] == ';' || char.IsWhiteSpace(inner[i])))
            {
                i++;
            }

            if (i >= inner.Length)
            {
                break;
            }

            if (inner[i] == '"')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < inner.Length)
                {
                    if (inner[i] == '"')
                    {
                        if (i + 1 < inner.Length && inner[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(inner[i++]);
                }

                values.Add(sb.ToString());
            }
            else
            {
                int start = i;
                while (i < inner.Length && inner[i] != ';')
                {
                    i++;
                }

                values.Add(inner[start..i].Trim());
            }
        }

        return values;
    }

    private static double ParseInvariant(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double value) ? value : 0;

    private static void AssembleValidations(
        Workbook workbook,
        Dictionary<string, ValidationRule> rules,
        Dictionary<(string Sheet, string Name), List<(int Row, int Column)>> cells)
    {
        foreach (var ((sheetName, ruleName), positions) in cells)
        {
            if (!rules.TryGetValue(ruleName, out var rule) || positions.Count == 0)
            {
                continue;
            }

            var sheet = workbook.FindSheet(sheetName);
            if (sheet is null)
            {
                continue;
            }

            // The bounding rectangle of all cells that referenced the rule is its target range.
            int top = positions.Min(p => p.Row);
            int bottom = positions.Max(p => p.Row);
            int left = positions.Min(p => p.Column);
            int right = positions.Max(p => p.Column);
            var target = new CellRange(new CellAddress(left, top), new CellAddress(right, bottom));

            sheet.AddValidation(new Data.DataValidation(rule.Kind, target)
            {
                Operator = rule.Operator,
                Operand1 = rule.Operand1,
                Operand2 = rule.Operand2,
                ListValues = rule.ListValues,
                ListSource = rule.ListSource,
                InputMessage = rule.InputMessage,
                ErrorMessage = rule.ErrorMessage,
                Response = rule.Response,
            });
        }
    }

    // ---- table:database-range (filter + sort, PRD §8.10–§8.12) -----------

    /// <summary>
    /// Emit one <c>table:database-range</c> per worksheet table and per sheet-level AutoFilter,
    /// carrying the filter region, the active filter conditions, and the sort order so they
    /// round-trip. Filtering sets row visibility (written as <c>table:visibility="filter"</c> by
    /// the row writer); reopening reapplies the filter so hidden rows stay hidden.
    /// </summary>
    private static void AddDatabaseRanges(XElement spreadsheet, Workbook workbook)
    {
        var ranges = new List<XElement>();
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var table in sheet.Tables)
            {
                ranges.Add(WriteDatabaseRange(sheet.Name, table.Name, table.Range, table.HasHeader, table.Filter, table.Sort));
            }

            if (sheet.ActiveFilter is { } region)
            {
                bool hasHeader = sheet.ActiveFilterSpec?.HasHeader ?? sheet.ActiveSort?.HasHeader ?? true;
                ranges.Add(WriteDatabaseRange(sheet.Name, name: null, region, hasHeader, sheet.ActiveFilterSpec, sheet.ActiveSort));
            }
        }

        if (ranges.Count == 0)
        {
            return;
        }

        var container = new XElement(Table + DatabaseRanges);
        foreach (var range in ranges)
        {
            container.Add(range);
        }

        spreadsheet.Add(container);
    }

    private static XElement WriteDatabaseRange(string sheetName, string? name, CellRange range, bool hasHeader, FilterSpec? filter, SortSpec? sort)
    {
        var element = new XElement(Table + DatabaseRange,
            new XAttribute(Table + TargetRangeAddress, OdfRangeAddress(range.WithSheet(sheetName), sheetName)),
            new XAttribute(Table + DisplayFilterButtons, "true"),
            new XAttribute(Table + ContainsHeader, hasHeader ? "true" : "false"));

        // table:name is required by ODF; anonymous sheet AutoFilters get a stable sentinel
        // so a reader can tell a real table apart from a bare filter region.
        element.Add(new XAttribute(Table + "name", name ?? AnonymousDatabaseRangeName));

        if (sort is { Levels.Count: > 0 })
        {
            element.Add(WriteSort(sort, range.Left));
        }

        if (filter is { Columns.Count: > 0 })
        {
            element.Add(WriteFilter(filter, range.Left));
        }

        return element;
    }

    private static XElement WriteSort(SortSpec sort, int rangeLeft)
    {
        var element = new XElement(Table + Sort);
        foreach (var level in sort.Levels)
        {
            element.Add(new XElement(Table + SortBy,
                new XAttribute(Table + FieldNumber, level.Column - rangeLeft),
                new XAttribute(Table + Order, level.Ascending ? "ascending" : "descending")));
        }

        return element;
    }

    private static XElement WriteFilter(FilterSpec filter, int rangeLeft)
    {
        // AND across columns; within a column, a value checklist is OR'd '=' conditions.
        var and = new XElement(Table + FilterAnd);
        foreach (var (column, columnFilter) in filter.Columns.OrderBy(kv => kv.Key))
        {
            int field = column - rangeLeft;

            if (columnFilter.AllowedValues is { Count: > 0 } allowed)
            {
                if (allowed.Count == 1)
                {
                    and.Add(Condition(field, "=", allowed.First()));
                }
                else
                {
                    var or = new XElement(Table + FilterOr);
                    foreach (var value in allowed)
                    {
                        or.Add(Condition(field, "=", value));
                    }

                    and.Add(or);
                }
            }

            if (columnFilter.Condition is { Length: > 0 } condition)
            {
                var (op, value) = SplitCondition(condition);
                and.Add(Condition(field, op, value));
            }
        }

        return new XElement(Table + Filter, and);

        static XElement Condition(int field, string op, string value) => new(
            Table + FilterCondition,
            new XAttribute(Table + FieldNumber, field),
            new XAttribute(Table + Operator, op),
            new XAttribute(Table + "value", value));
    }

    /// <summary>
    /// Split a SUMIF-style criteria string (<c>"&gt;5"</c>, <c>"&lt;=10"</c>, <c>"&lt;&gt;x"</c>, plain
    /// <c>"text"</c>) into an ODF operator and its value. Unprefixed criteria are an equality match.
    /// </summary>
    private static (string Operator, string Value) SplitCondition(string condition)
    {
        foreach (var (prefix, op) in ConditionOperators)
        {
            if (condition.StartsWith(prefix, StringComparison.Ordinal))
            {
                return (op, condition[prefix.Length..]);
            }
        }

        return ("=", condition);
    }

    private static string JoinCondition(string op, string value) => op switch
    {
        "=" => value,
        "!=" => "<>" + value,
        _ => op + value,
    };

    private static readonly (string Prefix, string Operator)[] ConditionOperators =
    {
        (">=", ">="), ("<=", "<="), ("<>", "!="), (">", ">"), ("<", "<"), ("=", "="),
    };

    private const string AnonymousDatabaseRangeName = "__Anonymous_Sheet__";

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

    // ---- table:database-range (read side) --------------------------------

    private static void ReadDatabaseRanges(XElement container, Workbook workbook)
    {
        foreach (var element in container.Elements(Table + DatabaseRange))
        {
            ReadDatabaseRange(element, workbook);
        }
    }

    private static void ReadDatabaseRange(XElement element, Workbook workbook)
    {
        string? address = element.Attribute(Table + TargetRangeAddress)?.Value;
        if (address is null)
        {
            return;
        }

        string internalRef = OpenFormulaTranslator.FromOpenFormula("of:=[" + address.TrimStart('$') + "]");
        if (!CellRange.TryParse(internalRef, out var range) || range.Sheet is null)
        {
            return;
        }

        var sheet = workbook.FindSheet(range.Sheet);
        if (sheet is null)
        {
            return;
        }

        bool hasHeader = element.Attribute(Table + ContainsHeader)?.Value != "false";

        // Normalize to a plain relative, sheet-less range so it compares equal to one built in code.
        var localRange = new CellRange(
            new CellAddress(range.Left, range.Top),
            new CellAddress(range.Right, range.Bottom));

        var sort = ReadSort(element, localRange.Left, hasHeader);
        var filter = ReadFilter(element, localRange.Left, hasHeader);

        string? name = element.Attribute(Table + "name")?.Value;
        if (name is not null && name != AnonymousDatabaseRangeName)
        {
            // A named database range is a Cellestra table; merge filter/sort onto it (or add it).
            var existing = sheet.Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (existing is not null)
            {
                sheet.ReplaceTable(existing, existing with { Filter = filter, Sort = sort });
            }
            else if (!sheet.Tables.Any(t => t.Range.Intersects(localRange)))
            {
                sheet.AddTable(new DataTable(name, localRange, hasHeader) { Filter = filter, Sort = sort });
            }
        }
        else
        {
            // An anonymous database range is the sheet-level AutoFilter region.
            sheet.ActiveFilter = localRange;
            sheet.ActiveFilterSpec = filter;
            sheet.ActiveSort = sort;
        }

        // Reapply the filter so rows it hides come back hidden on open (PRD §8.12).
        if (filter is { Columns.Count: > 0 })
        {
            FilterEngine.Apply(sheet, localRange, filter, CultureInfo.InvariantCulture);
        }
    }

    private static SortSpec? ReadSort(XElement databaseRange, int rangeLeft, bool hasHeader)
    {
        var sortElement = databaseRange.Element(Table + Sort);
        if (sortElement is null)
        {
            return null;
        }

        var levels = new List<SortLevel>();
        foreach (var by in sortElement.Elements(Table + SortBy))
        {
            if (int.TryParse(by.Attribute(Table + FieldNumber)?.Value, out int field))
            {
                bool ascending = by.Attribute(Table + Order)?.Value != "descending";
                levels.Add(new SortLevel(rangeLeft + field, ascending));
            }
        }

        return levels.Count > 0 ? new SortSpec(levels, hasHeader) : null;
    }

    private static FilterSpec? ReadFilter(XElement databaseRange, int rangeLeft, bool hasHeader)
    {
        var filterElement = databaseRange.Element(Table + Filter);
        var top = filterElement?.Element(Table + FilterAnd) ?? filterElement?.Element(Table + FilterOr);
        if (top is null)
        {
            return null;
        }

        // Per field, gather '=' values into a checklist and the first relational op into a condition.
        var allowed = new Dictionary<int, HashSet<string>>();
        var conditions = new Dictionary<int, string>();

        void Absorb(XElement condition)
        {
            if (!int.TryParse(condition.Attribute(Table + FieldNumber)?.Value, out int field))
            {
                return;
            }

            string op = condition.Attribute(Table + Operator)?.Value ?? "=";
            string value = condition.Attribute(Table + "value")?.Value ?? string.Empty;
            int column = rangeLeft + field;

            if (op == "=")
            {
                (allowed.TryGetValue(column, out var set) ? set : allowed[column] = new HashSet<string>(StringComparer.Ordinal)).Add(value);
            }
            else if (!conditions.ContainsKey(column))
            {
                conditions[column] = JoinCondition(op, value);
            }
        }

        foreach (var node in top.Elements())
        {
            if (node.Name == Table + FilterCondition)
            {
                Absorb(node);
            }
            else if (node.Name == Table + FilterOr || node.Name == Table + FilterAnd)
            {
                foreach (var nested in node.Elements(Table + FilterCondition))
                {
                    Absorb(nested);
                }
            }
        }

        var columns = new Dictionary<int, ColumnFilter>();
        foreach (int column in allowed.Keys.Union(conditions.Keys))
        {
            columns[column] = new ColumnFilter
            {
                AllowedValues = allowed.TryGetValue(column, out var set) ? set : null,
                Condition = conditions.TryGetValue(column, out var cond) ? cond : null,
            };
        }

        return columns.Count > 0 ? new FilterSpec(columns, hasHeader) : null;
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

        ReadColumns(table, sheet, columnWidths);
        ReadRows(table, sheet, styleLookup, rowHeights, validationCells);

        // If the table embeds a chart object, attach it so the sheet opens as a chart sheet.
        OdsCharts.ReadChart(table, package, sheet);
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
            (validationCells.TryGetValue(key, out var list) ? list : validationCells[key] = new List<(int, int)>())
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
            case "INF": value = double.PositiveInfinity; return true;
            case "-INF": value = double.NegativeInfinity; return true;
            case "NaN": value = double.NaN; return true;
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
