namespace Cellestra.Core.Styling;

/// <summary>
/// Classifies a number-format code into a <see cref="NumberFormatKind"/>. This drives the
/// command bar's Numbers group and the ODF data-style mapping; the actual rendering lives in
/// <c>NumberFormatter</c>.
/// </summary>
public static class NumberFormatParser
{
    public static NumberFormatKind Classify(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, "General", StringComparison.OrdinalIgnoreCase))
        {
            return NumberFormatKind.General;
        }

        if (code == "@")
        {
            return NumberFormatKind.Text;
        }

        // A currency-locale token is a currency token by definition ("[$€-407]#,##0.00"), whatever
        // letters its locale tag happens to contain. Checked before the pattern is stripped, since
        // stripping is what removes it.
        if (code.Contains("[$", StringComparison.Ordinal))
        {
            return NumberFormatKind.Currency;
        }

        // Only the pattern letters decide the kind — a letter inside a literal is a letter the format
        // prints, not a field it fills. `0" days"` and `0.0;[RED]-0.0` were both read as dates on the
        // strength of their "d", which made a number render as a date, not merely round-trip poorly.
        string pattern = WithoutLiterals(code);

        bool hasDate = ContainsAny(pattern, 'y', 'Y', 'd', 'D');
        bool hasMonth = pattern.Contains('m', StringComparison.Ordinal) || pattern.Contains('M', StringComparison.Ordinal);
        bool hasTime = ContainsAny(pattern, 'h', 'H', 's', 'S');

        if (hasDate && hasTime)
        {
            return NumberFormatKind.DateTime;
        }

        if (hasDate || (hasMonth && !hasTime))
        {
            return NumberFormatKind.Date;
        }

        if (hasTime)
        {
            return NumberFormatKind.Time;
        }

        if (pattern.Contains('%', StringComparison.Ordinal))
        {
            return NumberFormatKind.Percent;
        }

        if (ContainsCurrencySymbol(pattern))
        {
            return NumberFormatKind.Currency;
        }

        if (pattern.Contains('E', StringComparison.OrdinalIgnoreCase) &&
            (pattern.Contains('0', StringComparison.Ordinal) || pattern.Contains('#', StringComparison.Ordinal)))
        {
            return NumberFormatKind.Scientific;
        }

        if (pattern.Contains('/', StringComparison.Ordinal) && pattern.Contains('#', StringComparison.Ordinal))
        {
            return NumberFormatKind.Fraction;
        }

        if (pattern.Contains('0', StringComparison.Ordinal) || pattern.Contains('#', StringComparison.Ordinal))
        {
            return NumberFormatKind.Number;
        }

        return NumberFormatKind.General;
    }

    /// <summary>
    /// The code with its literal runs removed: quoted text (<c>"days"</c>), bracketed tokens
    /// (<c>[RED]</c>, <c>[$€-407]</c>, <c>[&gt;100]</c>), and backslash-escaped characters (<c>\d</c>).
    /// What remains is the pattern the format is actually built from.
    /// </summary>
    private static string WithoutLiterals(string code)
    {
        if (code.IndexOfAny(['"', '[', '\\']) < 0)
        {
            return code;
        }

        var kept = new System.Text.StringBuilder(code.Length);
        for (int i = 0; i < code.Length; i++)
        {
            switch (code[i])
            {
                case '"':
                    i = Skip(code, i + 1, '"');
                    break;
                case '[':
                    int end = Skip(code, i + 1, ']');
                    // Not every bracket is a literal: [h]/[mm]/[ss] are the elapsed-time tokens, and
                    // they are the pattern. Colours, conditions, and locale tags ([RED], [<100],
                    // [$€-407]) are not, and are what this is here to ignore.
                    var inside = code.AsSpan(i + 1, Math.Max(0, end - i - 1));
                    if (inside.Length > 0 && IsElapsedTime(inside))
                    {
                        kept.Append(inside);
                    }

                    i = end;
                    break;
                case '\\':
                    i++; // the escaped character is a literal
                    break;
                default:
                    kept.Append(code[i]);
                    break;
            }
        }

        return kept.ToString();
    }

    private static bool IsElapsedTime(ReadOnlySpan<char> inside)
    {
        foreach (char c in inside)
        {
            if (c is not ('h' or 'H' or 'm' or 'M' or 's' or 'S'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The index of <paramref name="close"/> at or after <paramref name="from"/>, or the last index when unterminated.</summary>
    private static int Skip(string code, int from, char close)
    {
        int end = code.IndexOf(close, from);
        return end < 0 ? code.Length - 1 : end;
    }

    private static bool ContainsCurrencySymbol(string code)
    {
        foreach (char c in code)
        {
            if (c is '$' or '€' or '£' or '¥' or '¤')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string code, params char[] chars)
    {
        foreach (char c in code)
        {
            if (Array.IndexOf(chars, c) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
