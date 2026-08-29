using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    [property: JsonPropertyName("format")] string? Format);

/// <summary>
/// Machine-readable output for every command (<c>--json</c>). One serializer configuration, one
/// place that knows how a cell is spelled, so the shape an agent learns from <c>get</c> is the shape
/// it gets back from <c>set</c>, <c>apply</c>, <c>validate</c>, and <c>diff</c>.
/// </summary>
public static class Report
{
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
            format);
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
            tables = sheet.Tables.Count > 0 ? sheet.Tables.Select(t => new { name = t.Name, range = t.Range.ToString(), header = t.HasHeader }) : null,
        };
    }

    public static object NamedRange(NamedRange named) => new
    {
        name = named.Name,
        target = named.Target.ToString(),
        scope = named.IsGlobal ? "workbook" : named.Scope,
    };
}
