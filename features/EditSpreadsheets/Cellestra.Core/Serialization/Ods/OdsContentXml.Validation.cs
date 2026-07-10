using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Model;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods;

public static partial class OdsContentXml
{
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

            string items = string.Join(";", (validation.ListValues ?? [])
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
}
