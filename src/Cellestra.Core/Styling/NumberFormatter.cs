using System.Globalization;
using System.Text;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Values;

namespace Cellestra.Core.Styling;

/// <summary>
/// Renders a <see cref="CellValue"/> for display under a number-format code and locale
/// (PRD §8.3). The single formatting path shared by the grid, the status bar, and the
/// TEXT() function, so what the user sees and what TEXT() returns never diverge.
/// </summary>
public static class NumberFormatter
{
    public static string Format(CellValue value, NumberFormat format, CultureInfo culture) =>
        Format(value, format.Code, culture);

    public static string Format(CellValue value, string code, CultureInfo culture)
    {
        switch (value.Type)
        {
            case CellValueType.Empty:
                return string.Empty;
            case CellValueType.Error:
                return value.AsError.Token;
            case CellValueType.Boolean:
                return value.AsBoolean ? "TRUE" : "FALSE";
            case CellValueType.Text:
                return value.AsText;
        }

        double number = value.AsNumber;
        if (string.IsNullOrEmpty(code) || string.Equals(code, "General", StringComparison.OrdinalIgnoreCase))
        {
            return FormatGeneral(number, culture);
        }

        if (code == "@")
        {
            return FormatGeneral(number, culture);
        }

        var kind = NumberFormatParser.Classify(code);
        if (kind is NumberFormatKind.Date or NumberFormatKind.Time or NumberFormatKind.DateTime)
        {
            return DateConversion.DateFromSerial(number).ToString(ExcelDateFormatToNet(code), culture);
        }

        try
        {
            return number.ToString(code, culture);
        }
        catch (FormatException)
        {
            return FormatGeneral(number, culture);
        }
    }

    /// <summary>General number display: integers plainly, otherwise up to 10 trimmed decimals.</summary>
    public static string FormatGeneral(double number, CultureInfo culture)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return number.ToString(culture);
        }

        if (number == System.Math.Floor(number) && System.Math.Abs(number) < 1e15)
        {
            return ((long)number).ToString(culture);
        }

        return number.ToString("0.##########", culture);
    }

    /// <summary>Map Excel/ODF date tokens to a .NET format string (longest match first).</summary>
    public static string ExcelDateFormatToNet(string code)
    {
        var tokens = new (string From, string To)[]
        {
            ("yyyy", "yyyy"), ("yy", "yy"),
            ("mmmm", "MMMM"), ("mmm", "MMM"), ("mm", "MM"), ("m", "M"),
            ("dddd", "dddd"), ("ddd", "ddd"), ("dd", "dd"), ("d", "d"),
            ("hh", "HH"), ("h", "H"),
            ("ss", "ss"), ("s", "s"),
        };

        string lower = code.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        int i = 0;
        while (i < lower.Length)
        {
            bool matched = false;
            foreach (var (from, to) in tokens)
            {
                if (i + from.Length <= lower.Length && string.CompareOrdinal(lower, i, from, 0, from.Length) == 0)
                {
                    sb.Append(to);
                    i += from.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                sb.Append(code[i]);
                i++;
            }
        }

        return sb.ToString();
    }
}
