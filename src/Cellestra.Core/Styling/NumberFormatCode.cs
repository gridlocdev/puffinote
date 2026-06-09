using System.Text;

namespace Cellestra.Core.Styling;

/// <summary>
/// Pure transforms on a numeric format <em>code</em> — used by the "increase/decrease decimal places"
/// commands so the bar can step the precision of a number format without rebuilding it. Operates on
/// the integer/fraction part of the first numeric run (the mantissa), leaving prefixes/suffixes such
/// as a currency symbol, <c>%</c>, or the <c>E+00</c> exponent untouched, and never alters date/time
/// codes. Mirrors how LibreOffice's increase/decrease-decimals toolbar buttons adjust the code.
/// </summary>
public static class NumberFormatCode
{
    /// <summary>Add one decimal place to a numeric code (General/empty start from two, like the toolbar).</summary>
    public static string IncreaseDecimals(string? code) => AdjustDecimals(code, +1);

    /// <summary>Remove one decimal place from a numeric code (no-op once there are none).</summary>
    public static string DecreaseDecimals(string? code) => AdjustDecimals(code, -1);

    /// <summary>
    /// Step the fraction-digit count of <paramref name="code"/> by <paramref name="delta"/> (clamped at
    /// zero). Date/time and text codes are returned unchanged. A General/empty code seeds from the
    /// default thousands pattern so the first "more decimals" tap yields a sensible "#,##0.0".
    /// </summary>
    public static string AdjustDecimals(string? code, int delta)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, "General", StringComparison.OrdinalIgnoreCase))
        {
            // Seed from a plain grouped number so stepping up first produces "#,##0.0", down stays "#,##0".
            code = "#,##0";
        }

        var kind = NumberFormatParser.Classify(code);
        if (kind is NumberFormatKind.Date or NumberFormatKind.Time or NumberFormatKind.DateTime or NumberFormatKind.Text)
        {
            return code; // decimals are meaningless here; leave the code alone
        }

        // Find the first numeric run — a maximal stretch of '#', '0', the grouping ',', and at most one
        // '.' — then rewrite just its fraction. Everything before/after (currency, %, E+00) is preserved.
        int start = -1;
        int end = -1;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            bool isMantissa = c is '#' or '0' or ',' or '.';
            if (isMantissa && start < 0)
            {
                start = i;
            }

            if (isMantissa)
            {
                end = i;
            }
            else if (start >= 0)
            {
                break; // the run ended; ignore any later numeric chars (e.g. the "00" of E+00)
            }
        }

        if (start < 0)
        {
            return code; // nothing number-like to adjust
        }

        string prefix = code[..start];
        string mantissa = code[start..(end + 1)];
        string suffix = code[(end + 1)..];

        int dot = mantissa.IndexOf('.');
        string integerPart = dot < 0 ? mantissa : mantissa[..dot];
        int fractionDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
        int target = Math.Max(0, fractionDigits + delta);

        var sb = new StringBuilder(prefix);
        sb.Append(integerPart);
        if (target > 0)
        {
            sb.Append('.');
            sb.Append('0', target);
        }

        sb.Append(suffix);
        return sb.ToString();
    }
}
