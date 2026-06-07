namespace Cellestra.Core.Styling;

/// <summary>
/// Classifies a number-format code into a <see cref="NumberFormatKind"/>. This drives the
/// ribbon's Number group and the ODF data-style mapping; the actual rendering lives in
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

        bool hasDate = ContainsAny(code, 'y', 'Y', 'd', 'D');
        bool hasMonth = code.Contains('m', StringComparison.Ordinal) || code.Contains('M', StringComparison.Ordinal);
        bool hasTime = ContainsAny(code, 'h', 'H', 's', 'S');

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

        if (code.Contains('%', StringComparison.Ordinal))
        {
            return NumberFormatKind.Percent;
        }

        if (ContainsCurrencySymbol(code))
        {
            return NumberFormatKind.Currency;
        }

        if (code.Contains('E', StringComparison.OrdinalIgnoreCase) &&
            (code.Contains('0', StringComparison.Ordinal) || code.Contains('#', StringComparison.Ordinal)))
        {
            return NumberFormatKind.Scientific;
        }

        if (code.Contains('/', StringComparison.Ordinal) && code.Contains('#', StringComparison.Ordinal))
        {
            return NumberFormatKind.Fraction;
        }

        if (code.Contains('0', StringComparison.Ordinal) || code.Contains('#', StringComparison.Ordinal))
        {
            return NumberFormatKind.Number;
        }

        return NumberFormatKind.General;
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
