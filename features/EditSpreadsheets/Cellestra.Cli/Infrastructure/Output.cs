using System.Text;
using System.Text.Json;
using Cellestra.Core.Model;

namespace Cellestra.Cli.Infrastructure;

/// <summary>How read commands render a grid.</summary>
public enum OutputFormat
{
    Table,
    Csv,
    Tsv,
    Json,
}

/// <summary>
/// Pipe-friendly rendering of a rectangular block of cells as a table, CSV, TSV, or JSON. The
/// table form labels columns (A, B, …) and rows for at-a-glance reading; csv/tsv/json are for
/// scripts and downstream tools.
/// </summary>
public static class Output
{
    public static OutputFormat ParseFormat(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" or "table" => OutputFormat.Table,
        "csv" => OutputFormat.Csv,
        "tsv" => OutputFormat.Tsv,
        "json" => OutputFormat.Json,
        _ => throw new CliException($"Unknown format '{value}'. Use table, csv, tsv, or json."),
    };

    /// <summary>Render a grid; <paramref name="cells"/>[r][c] holds display strings.</summary>
    public static string Render(OutputFormat format, string sheetName, CellRange range, string[][] cells) => format switch
    {
        OutputFormat.Csv => Delimited(cells, ',', quote: true),
        OutputFormat.Tsv => Delimited(cells, '\t', quote: false),
        OutputFormat.Json => Json(sheetName, range, cells),
        _ => Table(range, cells),
    };

    private static string Delimited(string[][] cells, char delimiter, bool quote)
    {
        var sb = new StringBuilder();
        foreach (var row in cells)
        {
            for (int c = 0; c < row.Length; c++)
            {
                if (c > 0)
                {
                    sb.Append(delimiter);
                }

                sb.Append(quote ? QuoteCsv(row[c], delimiter) : row[c].Replace("\t", " ", StringComparison.Ordinal));
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string QuoteCsv(string field, char delimiter)
    {
        bool mustQuote = field.IndexOf(delimiter) >= 0 || field.Contains('"', StringComparison.Ordinal) ||
            field.Contains('\n', StringComparison.Ordinal) || field.Contains('\r', StringComparison.Ordinal);
        return mustQuote ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : field;
    }

    private static string Json(string sheetName, CellRange range, string[][] cells)
    {
        var payload = new
        {
            sheet = sheetName,
            range = range.ToString(),
            rows = cells,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static string Table(CellRange range, string[][] cells)
    {
        int columns = range.ColumnCount;
        int rows = range.RowCount;

        // Column headers (A, B, …) and a row-number gutter.
        var headers = new string[columns];
        for (int c = 0; c < columns; c++)
        {
            headers[c] = SheetDimensions.ColumnToLetters(range.Left + c);
        }

        string gutterHeader = string.Empty;
        var rowLabels = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            rowLabels[r] = (range.Top + r + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        int gutter = Math.Max(gutterHeader.Length, rowLabels.Length == 0 ? 0 : rowLabels.Max(l => l.Length));
        var widths = new int[columns];
        for (int c = 0; c < columns; c++)
        {
            widths[c] = headers[c].Length;
            for (int r = 0; r < rows; r++)
            {
                widths[c] = Math.Max(widths[c], cells[r][c].Length);
            }
        }

        var sb = new StringBuilder();
        AppendRow(sb, gutter, widths, gutterHeader, headers);
        AppendSeparator(sb, gutter, widths);
        for (int r = 0; r < rows; r++)
        {
            AppendRow(sb, gutter, widths, rowLabels[r], cells[r]);
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int gutter, int[] widths, string label, string[] values)
    {
        sb.Append(label.PadLeft(gutter)).Append(" │");
        for (int c = 0; c < widths.Length; c++)
        {
            sb.Append(' ').Append(values[c].PadRight(widths[c])).Append(' ');
            if (c < widths.Length - 1)
            {
                sb.Append('│');
            }
        }

        sb.Append('\n');
    }

    private static void AppendSeparator(StringBuilder sb, int gutter, int[] widths)
    {
        sb.Append(new string('─', gutter)).Append("─┼");
        for (int c = 0; c < widths.Length; c++)
        {
            sb.Append(new string('─', widths[c] + 2));
            if (c < widths.Length - 1)
            {
                sb.Append('┼');
            }
        }

        sb.Append('\n');
    }
}
