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
            // Time/date-time formats render hours, minutes and seconds, so they need the time-of-day
            // fraction preserved (FromSerial). A pure date drops it (DateFromSerial) as before.
            // A serial outside DateTime's representable range (roughly years 1–9999) can't name a real
            // date — LibreOffice falls back to a plain number rather than erroring, so do the same
            // instead of letting AddDays throw and crash the whole format/TEXT() call.
            if (!DateConversion.TryToDateTime(number, dateOnly: kind == NumberFormatKind.Date, out var when))
            {
                return FormatGeneral(number, culture);
            }

            try
            {
                return when.ToString(ExcelDateFormatToNet(code), culture);
            }
            catch (FormatException)
            {
                return FormatGeneral(number, culture);
            }
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

    /// <summary>
    /// Map Excel/ODF date-time tokens to a .NET format string. Two pieces of Excel semantics matter:
    /// an <c>AM/PM</c> (or <c>A/P</c>) token means "12-hour clock + a literal AM/PM" — .NET <c>tt</c>
    /// with lower-case <c>h</c>; and <c>m</c>/<c>mm</c> means *minutes* (lower-case <c>m</c> in .NET)
    /// when it sits next to an hour or seconds token, else *month* (upper-case <c>M</c>). Getting the
    /// first wrong rendered "AM/PM" as the month-number "A1/P1"; getting the second wrong rendered the
    /// minutes as the month.
    /// </summary>
    public static string ExcelDateFormatToNet(string code)
    {
        string lower = code.ToLowerInvariant();
        bool twelveHour = lower.Contains("am/pm") || lower.Contains("a/p");

        var sb = new StringBuilder(lower.Length);
        int i = 0;
        while (i < lower.Length)
        {
            if (StartsWith(lower, i, "am/pm")) { sb.Append("tt"); i += 5; continue; }
            if (StartsWith(lower, i, "a/p")) { sb.Append('t'); i += 3; continue; }

            char c = lower[i];
            switch (c)
            {
                case 'y':
                    {
                        int n = RunLength(lower, i, 'y');
                        sb.Append(n >= 3 ? "yyyy" : "yy");
                        i += n;
                        break;
                    }
                case 'd':
                    {
                        int n = RunLength(lower, i, 'd');
                        sb.Append(n >= 4 ? "dddd" : n == 3 ? "ddd" : n == 2 ? "dd" : "d");
                        i += n;
                        break;
                    }
                case 'h':
                    {
                        int n = RunLength(lower, i, 'h');
                        char h = twelveHour ? 'h' : 'H';
                        sb.Append(h, System.Math.Min(n, 2));
                        i += n;
                        break;
                    }
                case 's':
                    {
                        int n = RunLength(lower, i, 's');
                        sb.Append('s', System.Math.Min(n, 2));
                        i += n;
                        break;
                    }
                case 'm':
                    {
                        int n = RunLength(lower, i, 'm');
                        if (n >= 3)
                        {
                            sb.Append(n >= 4 ? "MMMM" : "MMM"); // month names are never minutes
                        }
                        else
                        {
                            char m = IsMinuteContext(lower, i, i + n) ? 'm' : 'M';
                            sb.Append(m, n);
                        }

                        i += n;
                        break;
                    }
                default:
                    sb.Append(code[i]); // preserve the original-case literal (separators, "AM/PM" already handled)
                    i++;
                    break;
            }
        }

        string pattern = sb.ToString();

        // .NET reads a single-character format string as a *standard* date specifier (e.g. "d" =
        // short date, "H" = invalid → throws), not the custom token we built. Escape it with '%'
        // so a code like "h" renders the hour instead of crashing the format/TEXT()/grid-paint call.
        return pattern.Length == 1 ? "%" + pattern : pattern;
    }

    private static bool StartsWith(string s, int index, string token) =>
        index + token.Length <= s.Length && string.CompareOrdinal(s, index, token, 0, token.Length) == 0;

    private static int RunLength(string s, int index, char c)
    {
        int n = 0;
        while (index + n < s.Length && s[index + n] == c)
        {
            n++;
        }

        return n;
    }

    /// <summary>
    /// True when an <c>m</c>/<c>mm</c> run at <c>[start, end)</c> is minutes rather than a month: the
    /// nearest letter before it (skipping ':' / '.' / ' ' separators) is an hour token, or the nearest
    /// letter after it is a seconds token.
    /// </summary>
    private static bool IsMinuteContext(string s, int start, int end)
    {
        for (int j = start - 1; j >= 0; j--)
        {
            char c = s[j];
            if (c is ':' or '.' or ' ' or '[' or ']')
            {
                continue;
            }

            if (c == 'h')
            {
                return true;
            }

            break;
        }

        for (int j = end; j < s.Length; j++)
        {
            char c = s[j];
            if (c is ':' or '.' or ' ' or '[' or ']')
            {
                continue;
            }

            if (c == 's')
            {
                return true;
            }

            break;
        }

        return false;
    }
}
