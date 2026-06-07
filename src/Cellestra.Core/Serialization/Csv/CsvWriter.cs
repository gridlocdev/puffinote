using System.Globalization;
using System.Text;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;

namespace Cellestra.Core.Serialization.Csv;

/// <summary>
/// Writes a worksheet to CSV. Exports the <b>displayed/computed</b> values, never formulas
/// (PRD §8.4): numbers/dates are formatted with each cell's number format and the active
/// locale. RFC 4180 quoting; LF line endings.
/// </summary>
public static class CsvWriter
{
    public static string Write(Workbook workbook, Worksheet sheet, CsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        options ??= CsvOptions.Default;
        var culture = workbook.Settings.EffectiveLocale;

        var used = sheet.GetUsedRange();
        var sb = new StringBuilder();
        if (used is null)
        {
            return string.Empty;
        }

        int lastRow = used.Value.Bottom;
        int lastColumn = used.Value.Right;

        for (int row = 0; row <= lastRow; row++)
        {
            for (int column = 0; column <= lastColumn; column++)
            {
                if (column > 0)
                {
                    sb.Append(options.Delimiter);
                }

                sb.Append(Quote(DisplayValue(workbook, sheet, row, column, culture), options.Delimiter));
            }

            sb.Append('\n'); // LF on export
        }

        return sb.ToString();
    }

    private static string DisplayValue(Workbook workbook, Worksheet sheet, int row, int column, CultureInfo culture)
    {
        var cell = sheet.Find(row, column);
        if (cell is null || cell.Value.IsEmpty)
        {
            return string.Empty;
        }

        var format = workbook.Styles.Get(cell.StyleId).NumberFormat;
        return NumberFormatter.Format(cell.Value, format, culture);
    }

    private static string Quote(string field, char delimiter)
    {
        bool mustQuote = field.IndexOf(delimiter) >= 0 ||
            field.IndexOf('"') >= 0 ||
            field.IndexOf('\n') >= 0 ||
            field.IndexOf('\r') >= 0;

        if (!mustQuote)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
