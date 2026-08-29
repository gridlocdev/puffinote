using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cellestra.Core;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;

namespace Cellestra.Cli.Infrastructure;

/// <summary>
/// One cell as a script or an agent wants to see it: the address, the typed value, the display
/// string the GUI would show, and — only when present — the formula source, the error token, and
/// the number-format code. Nulls are omitted on the wire, so a plain number is three keys.
/// </summary>
public sealed record CellReport(
    [property: JsonPropertyName("cell")] string Cell,
    [property: JsonPropertyName("value")] object? Value,
    [property: JsonPropertyName("display")] string Display,
    [property: JsonPropertyName("formula")] string? Formula,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("style")] IReadOnlyDictionary<string, object?>? Style = null);

/// <summary>
/// Machine-readable output for every command (<c>--json</c>). One serializer configuration, one
/// place that knows how a cell is spelled, so the shape an agent learns from <c>get</c> is the shape
/// it gets back from <c>set</c>, <c>apply</c>, <c>validate</c>, and <c>diff</c>.
/// </summary>
public static class Report
{
    /// <summary>
    /// The version of the JSON shapes this build speaks. Bump on a breaking change to any reply or
    /// to the <c>apply</c> document; additive keys don't bump it. Scripts read it from <c>man --json</c>.
    /// </summary>
    public const int ContractVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Serialize <paramref name="payload"/> to stdout as one indented JSON document.</summary>
    public static void Write(object payload) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));

    public static string Address(string sheet, int row, int column) =>
        new CellAddress(column, row, sheet: sheet).ToString();

    /// <summary>Describe one cell; an empty position still reports (value null, display "").</summary>
    public static CellReport Cell(Workbook workbook, Worksheet sheet, int row, int column, CultureInfo culture)
    {
        var cell = sheet.Find(row, column);
        string address = Address(sheet.Name, row, column);
        if (cell is null)
        {
            return new CellReport(address, null, string.Empty, null, null, null);
        }

        var style = workbook.Styles.Get(cell.StyleId);
        string? format = style.NumberFormat.IsGeneral ? null : style.NumberFormat.Code;
        var styling = Style(style);
        var value = cell.Value;
        object? typed = value.IsNumber ? value.AsNumber
            : value.IsText ? value.AsText
            : value.IsBoolean ? value.AsBoolean
            : null;
        string display = value.IsEmpty ? string.Empty : NumberFormatter.Format(value, style.NumberFormat, culture);
        return new CellReport(
            address,
            typed,
            display,
            cell.IsFormula ? "=" + cell.Formula : null,
            value.IsError ? value.AsError.Token : null,
            format,
            styling);
    }

    /// <summary>
    /// A cell's formatting as the keys <c>apply</c>'s <c>style</c> op takes — only what differs from
    /// the default, so a plain cell has no <c>style</c> at all and a bold header is <c>{"bold": true}</c>.
    /// Reading it back is how a script confirms the styling it just wrote.
    /// </summary>
    public static IReadOnlyDictionary<string, object?>? Style(CellStyle style)
    {
        var d = new Dictionary<string, object?>();
        var font = style.Font;
        var basis = Font.Default;
        if (font.Bold)
        {
            d["bold"] = true;
        }

        if (font.Italic)
        {
            d["italic"] = true;
        }

        if (font.Underline)
        {
            d["underline"] = true;
        }

        if (font.Strikethrough)
        {
            d["strikethrough"] = true;
        }

        if (!string.Equals(font.Family, basis.Family, StringComparison.Ordinal))
        {
            d["font"] = font.Family;
        }

        if (font.SizePoints != basis.SizePoints)
        {
            d["size"] = font.SizePoints;
        }

        if (font.Color != basis.Color && font.Color != default)
        {
            d["color"] = font.Color.ToHex();
        }

        if (style.Fill.Background is { } fill)
        {
            d["fill"] = fill.ToHex();
        }

        if (style.Alignment.Horizontal != HorizontalAlignment.General)
        {
            d["align"] = style.Alignment.Horizontal.ToString().ToLowerInvariant();
        }

        if (style.Alignment.Vertical != VerticalAlignment.Bottom)
        {
            d["valign"] = style.Alignment.Vertical.ToString().ToLowerInvariant();
        }

        if (style.Alignment.WrapText)
        {
            d["wrap"] = true;
        }

        if (style.Border.HasAnyEdge)
        {
            var b = style.Border;
            static string Edge(BorderEdge e) => e.Style.ToString().ToLowerInvariant();
            bool uniform = b.Left == b.Right && b.Right == b.Top && b.Top == b.Bottom;
            d["border"] = uniform ? Edge(b.Left) : new { left = Edge(b.Left), right = Edge(b.Right), top = Edge(b.Top), bottom = Edge(b.Bottom) };
            var color = new[] { b.Left, b.Right, b.Top, b.Bottom }.FirstOrDefault(e => e.IsVisible)?.Color ?? ArgbColor.Black;
            if (color != ArgbColor.Black)
            {
                d["borderColor"] = color.ToHex();
            }
        }

        return d.Count == 0 ? null : d;
    }

    public static CellReport Cell(Workbook workbook, CellRef reference, CultureInfo culture)
    {
        var sheet = workbook.FindSheet(reference.Sheet)
            ?? throw new InvalidOperationException($"Sheet '{reference.Sheet}' not found.");
        return Cell(workbook, sheet, reference.Row, reference.Column, culture);
    }

    /// <summary>Every cell in the workbook whose value is an error, in sheet/row/column order.</summary>
    public static List<CellReport> Errors(Workbook workbook, CultureInfo culture)
    {
        var errors = new List<CellReport>();
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var (row, column, cell) in sheet.EnumerateCells())
            {
                if (cell.Value.IsError)
                {
                    errors.Add(Cell(workbook, sheet, row, column, culture));
                }
            }
        }

        return errors;
    }

    public static object Sheet(Worksheet sheet)
    {
        var used = sheet.GetUsedRange();
        return new
        {
            name = sheet.Name,
            usedRange = used?.ToString(),
            rows = used?.RowCount ?? 0,
            columns = used?.ColumnCount ?? 0,
            cells = sheet.CellCount,
            chart = sheet.IsChartSheet ? true : (bool?)null,
            frozenRows = sheet.FrozenRows > 0 ? sheet.FrozenRows : (int?)null,
            frozenColumns = sheet.FrozenColumns > 0 ? sheet.FrozenColumns : (int?)null,
            tables = sheet.Tables.Count > 0 ? sheet.Tables.Select(Table).ToList() : null,
            merges = sheet.MergedRegions.Count > 0 ? sheet.MergedRegions.Select(m => m.Range.ToString()).ToList() : null,
            widths = Layout(sheet.Columns, c => c.Width != Defaults.ColumnWidth, c => c.Width, SheetDimensions.ColumnToLetters),
            heights = Layout(sheet.Rows, r => r.Height != Defaults.RowHeight, r => r.Height, r => (r + 1).ToString(CultureInfo.InvariantCulture)),
            hiddenColumns = Hidden(sheet.Columns, c => c.Hidden, SheetDimensions.ColumnToLetters),
            hiddenRows = Hidden(sheet.Rows, r => r.Hidden, r => (r + 1).ToString(CultureInfo.InvariantCulture)),
            validations = sheet.Validations.Count > 0 ? sheet.Validations.Select(Validation).ToList() : null,
        };
    }

    public static object Table(Cellestra.Core.Data.Table t) => new
    {
        name = t.Name,
        range = t.Range.ToString(),
        header = t.HasHeader,
        headerColumn = t.HasHeaderColumn ? true : (bool?)null,
        bandedRows = t.HasBandedRows ? true : (bool?)null,
    };

    public static object Validation(Cellestra.Core.Data.DataValidation v) => new
    {
        range = v.Target.ToString(),
        kind = v.Kind.ToString().ToLowerInvariant(),
        @operator = v.Kind is Cellestra.Core.Data.ValidationKind.List or Cellestra.Core.Data.ValidationKind.Any ? null : v.Operator.ToString().ToLowerInvariant(),
        value1 = v.Kind is Cellestra.Core.Data.ValidationKind.List or Cellestra.Core.Data.ValidationKind.Any ? null : (double?)v.Operand1,
        value2 = v.Operator is Cellestra.Core.Data.ValidationOperator.Between or Cellestra.Core.Data.ValidationOperator.NotBetween
            && v.Kind is not (Cellestra.Core.Data.ValidationKind.List or Cellestra.Core.Data.ValidationKind.Any) ? (double?)v.Operand2 : null,
        list = v.ListValues,
        source = v.ListSource?.ToString(),
        response = v.Response == Cellestra.Core.Data.ValidationResponse.Stop ? null : v.Response.ToString().ToLowerInvariant(),
    };

    private static Dictionary<string, double>? Layout<T>(IReadOnlyDictionary<int, T> lines, Func<T, bool> differs, Func<T, double> size, Func<int, string> label)
    {
        var d = lines.Where(kv => differs(kv.Value)).OrderBy(kv => kv.Key)
            .ToDictionary(kv => label(kv.Key), kv => Math.Round(size(kv.Value), 1), StringComparer.Ordinal); // ODS stores cm; don't echo the conversion noise
        return d.Count == 0 ? null : d;
    }

    private static List<string>? Hidden<T>(IReadOnlyDictionary<int, T> lines, Func<T, bool> hidden, Func<int, string> label)
    {
        var list = lines.Where(kv => hidden(kv.Value)).OrderBy(kv => kv.Key).Select(kv => label(kv.Key)).ToList();
        return list.Count == 0 ? null : list;
    }

    public static object NamedRange(NamedRange named) => new
    {
        name = named.Name,
        target = named.Target.ToString(),
        scope = named.IsGlobal ? "workbook" : named.Scope,
    };
}
