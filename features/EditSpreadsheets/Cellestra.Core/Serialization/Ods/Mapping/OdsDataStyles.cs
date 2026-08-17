using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Styling;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods.Mapping;

/// <summary>
/// Maps Cellestra number-format codes to ODF data styles (<c>number:*-style</c>) and back.
/// Covers the common v1 cases — General, fixed decimals, percent, thousands, and ISO dates;
/// anything else falls back to General so files still open cleanly.
/// </summary>
public static class OdsDataStyles
{
    /// <summary>Build the ODF data-style element for a format, or null for General/Text.</summary>
    public static XElement? Build(string name, NumberFormat format)
    {
        if (format.IsGeneral)
        {
            return null;
        }

        // Text ("@") is a style, not the absence of one: it is what stops a cell parsing its content as
        // a number. Returning null here wrote no data style at all, so the cell reopened as Automatic
        // and its content went back to being interpreted.
        if (format.Code == "@")
        {
            return new XElement(Number + "text-style",
                new XAttribute(Style + "name", name),
                new XElement(Number + "text-content"));
        }

        return format.Kind switch
        {
            NumberFormatKind.Percent => Percentage(name, format.Code),
            NumberFormatKind.Currency => Currency(name, format.Code) ?? NumberStyle(name, format.Code),
            NumberFormatKind.Scientific => Scientific(name, format.Code),
            NumberFormatKind.Fraction => Fraction(name, format.Code),
            NumberFormatKind.Number => NumberStyle(name, format.Code),
            NumberFormatKind.Date or NumberFormatKind.DateTime or NumberFormatKind.Time => DateStyle(name, format.Code),
            _ => null,
        };
    }

    /// <summary>Reconstruct a format code from an ODF data-style element (best effort).</summary>
    public static string ToCode(XElement dataStyle)
    {
        string local = dataStyle.Name.LocalName;
        return local switch
        {
            "percentage-style" => PercentCode(dataStyle),
            "number-style" => NumberCode(dataStyle),
            "currency-style" => CurrencyCode(dataStyle),
            "date-style" => DateCode(dataStyle),
            "text-style" => "@",
            _ => "General",
        };
    }

    private static XElement NumberStyle(string name, string code)
    {
        int decimals = DecimalPlaces(code);
        bool grouping = code.Contains(',', StringComparison.Ordinal);
        var number = new XElement(Number + "number",
            new XAttribute(Number + "decimal-places", decimals),
            new XAttribute(Number + "min-integer-digits", 1));
        if (grouping)
        {
            number.Add(new XAttribute(Number + "grouping", "true"));
        }

        return new XElement(Number + "number-style", new XAttribute(Style + "name", name), number);
    }

    /// <summary>
    /// A <c>number:number-style</c> holding a <c>number:scientific-number</c> — ODF's exponent format.
    /// Written as a plain <c>number:number</c>, <c>0.00E+00</c> reopened as <c>0.00</c>: the exponent
    /// is carried by the element type, and there is nowhere else to put it.
    /// </summary>
    private static XElement Scientific(string name, string code)
    {
        int e = code.IndexOf('E', StringComparison.OrdinalIgnoreCase);
        int exponentDigits = 0;
        for (int i = e + 1; i < code.Length; i++)
        {
            if (code[i] is '0' or '#')
            {
                exponentDigits++;
            }
            else if (code[i] is not ('+' or '-'))
            {
                break;
            }
        }

        return new XElement(Number + "number-style",
            new XAttribute(Style + "name", name),
            new XElement(Number + "scientific-number",
                new XAttribute(Number + "decimal-places", DecimalPlaces(code)),
                new XAttribute(Number + "min-integer-digits", IntegerDigits(code)),
                new XAttribute(Number + "min-exponent-digits", Math.Max(1, exponentDigits))));
    }

    /// <summary>
    /// A <c>number:number-style</c> holding a <c>number:fraction</c> (<c># ?/?</c>). Cellestra doesn't
    /// render fractions, but a format it can't draw is still a format the file chose — dropping it to
    /// General on save would rewrite someone else's document to say something it didn't.
    /// </summary>
    private static XElement Fraction(string name, string code)
    {
        int slash = code.IndexOf('/', StringComparison.Ordinal);
        int numerator = CountPlaceholders(code, slash < 0 ? 0 : slash - 1, -1);
        int denominator = CountPlaceholders(code, slash + 1, +1);

        return new XElement(Number + "number-style",
            new XAttribute(Style + "name", name),
            new XElement(Number + "fraction",
                new XAttribute(Number + "min-integer-digits", code.Contains('#', StringComparison.Ordinal) ? 0 : 1),
                new XAttribute(Number + "min-numerator-digits", numerator),
                new XAttribute(Number + "min-denominator-digits", denominator)));
    }

    /// <summary>Count the run of <c>?</c>/<c>0</c>/<c>#</c> placeholders from <paramref name="start"/> in <paramref name="step"/>'s direction.</summary>
    private static int CountPlaceholders(string code, int start, int step)
    {
        int count = 0;
        for (int i = start; i >= 0 && i < code.Length && code[i] is '?' or '0' or '#'; i += step)
        {
            count++;
        }

        return Math.Max(1, count);
    }

    /// <summary>How many leading digit placeholders a code asks for — the <c>0</c>s before its decimal point.</summary>
    private static int IntegerDigits(string code)
    {
        int count = 0;
        foreach (char c in code)
        {
            if (c == '.')
            {
                break;
            }

            if (c == '0')
            {
                count++;
            }
        }

        return Math.Max(1, count);
    }

    /// <summary>
    /// A <c>number:currency-style</c> — the only ODF style that carries the currency symbol. Writing a
    /// currency format as a plain <c>number:number-style</c> drops the symbol, so the file reopened as a
    /// thousands-separated number: the format survived, the fact that it was money did not.
    /// </summary>
    /// <remarks>
    /// The code is split at its numeric core (the run of <c>#0,.</c>): what precedes and follows it is
    /// literal text, and whichever side holds the currency symbol becomes <c>number:currency-symbol</c>.
    /// That covers a leading symbol (<c>$#,##0.00</c>) and a trailing one (<c>#,##0.00 €</c>) alike.
    /// Returns null when the code has no numeric core to build around, leaving the caller its fallback.
    /// </remarks>
    private static XElement? Currency(string name, string code)
    {
        int start = code.IndexOfAny(['#', '0']);
        if (start < 0)
        {
            return null;
        }

        int end = start;
        while (end < code.Length && code[end] is '#' or '0' or ',' or '.')
        {
            end++;
        }

        string core = code[start..end];
        var style = new XElement(Number + "currency-style", new XAttribute(Style + "name", name));
        AddLiteral(style, code[..start]);
        style.Add(new XElement(Number + "number",
            new XAttribute(Number + "decimal-places", DecimalPlaces(core)),
            new XAttribute(Number + "min-integer-digits", 1),
            new XAttribute(Number + "grouping", core.Contains(',', StringComparison.Ordinal) ? "true" : "false")));
        AddLiteral(style, code[end..]);
        return style;
    }

    /// <summary>Append a literal run either side of the number, splitting the currency symbol out of it.</summary>
    private static void AddLiteral(XElement style, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var pending = new System.Text.StringBuilder();
        void Flush()
        {
            if (pending.Length > 0)
            {
                style.Add(new XElement(Number + "text", pending.ToString()));
                pending.Clear();
            }
        }

        foreach (char c in text)
        {
            if (IsCurrencySymbol(c))
            {
                Flush();
                style.Add(new XElement(Number + "currency-symbol", c.ToString()));
            }
            else
            {
                pending.Append(c);
            }
        }

        Flush();
    }

    private static bool IsCurrencySymbol(char c) => c is '$' or '€' or '£' or '¥' or '¤';

    private static XElement Percentage(string name, string code)
    {
        int decimals = DecimalPlaces(code);
        var number = new XElement(Number + "number",
            new XAttribute(Number + "decimal-places", decimals),
            new XAttribute(Number + "min-integer-digits", 1));
        if (code.Contains(',', StringComparison.Ordinal))
        {
            number.Add(new XAttribute(Number + "grouping", "true"));
        }

        return new XElement(Number + "percentage-style",
            new XAttribute(Style + "name", name),
            number,
            new XElement(Number + "text", "%"));
    }

    private static XElement DateStyle(string name, string code)
    {
        var style = new XElement(Number + "date-style", new XAttribute(Style + "name", name));
        // Walk the code, emitting year/month/day/separator pieces for the common tokens.
        string lower = code.ToLowerInvariant();
        int i = 0;
        while (i < lower.Length)
        {
            if (Starts(lower, i, "yyyy"))
            {
                style.Add(Year(true));
                i += 4;
            }
            else if (Starts(lower, i, "yy"))
            {
                style.Add(Year(false));
                i += 2;
            }
            else if (Starts(lower, i, "mmmm"))
            {
                style.Add(Month("long", textual: true));
                i += 4;
            }
            else if (Starts(lower, i, "mmm"))
            {
                style.Add(Month("short", textual: true));
                i += 3;
            }
            else if (Starts(lower, i, "mm"))
            {
                style.Add(Month("long", textual: false));
                i += 2;
            }
            else if (Starts(lower, i, "m"))
            {
                style.Add(Month("short", textual: false));
                i += 1;
            }
            else if (Starts(lower, i, "dd"))
            {
                style.Add(Day(true));
                i += 2;
            }
            else if (Starts(lower, i, "d"))
            {
                style.Add(Day(false));
                i += 1;
            }
            else
            {
                style.Add(new XElement(Number + "text", code[i].ToString()));
                i += 1;
            }
        }

        return style;
    }

    private static XElement Year(bool full) =>
        new(Number + "year", new XAttribute(Number + "style", full ? "long" : "short"));

    private static XElement Month(string style, bool textual) =>
        new(Number + "month",
            new XAttribute(Number + "style", style),
            new XAttribute(Number + "textual", textual ? "true" : "false"));

    private static XElement Day(bool full) =>
        new(Number + "day", new XAttribute(Number + "style", full ? "long" : "short"));

    private static bool Starts(string text, int index, string token) =>
        index + token.Length <= text.Length && string.CompareOrdinal(text, index, token, 0, token.Length) == 0;

    private static int DecimalPlaces(string code)
    {
        int dot = code.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = dot + 1; i < code.Length && (code[i] == '0' || code[i] == '#'); i++)
        {
            count++;
        }

        return count;
    }

    private static string NumberCode(XElement style)
    {
        if (style.Element(Number + "fraction") is { } fraction)
        {
            string whole = ReadDigits(fraction, "min-integer-digits", 0) > 0 ? "0" : "#";
            return whole + " " + new string('?', ReadDigits(fraction, "min-numerator-digits", 1))
                + "/" + new string('?', ReadDigits(fraction, "min-denominator-digits", 1));
        }

        if (style.Element(Number + "scientific-number") is { } scientific)
        {
            int decimals = ReadDecimals(scientific);
            string mantissa = new('0', ReadDigits(scientific, "min-integer-digits", 1));
            if (decimals > 0)
            {
                mantissa += "." + new string('0', decimals);
            }

            return mantissa + "E+" + new string('0', ReadDigits(scientific, "min-exponent-digits", 2));
        }

        return NumberCore(style.Element(Number + "number"));
    }

    private static int ReadDigits(XElement element, string attribute, int fallback) =>
        int.TryParse(element.Attribute(Number + attribute)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)
            ? d
            : fallback;

    /// <summary>The numeric part of a code — <c>#,##0.00</c> — from a <c>number:number</c> element.</summary>
    private static string NumberCore(XElement? number)
    {
        int decimals = ReadDecimals(number);
        bool grouping = number?.Attribute(Number + "grouping")?.Value == "true";
        string integer = grouping ? "#,##0" : "0";
        return decimals > 0 ? integer + "." + new string('0', decimals) : integer;
    }

    /// <summary>
    /// Rebuild a currency code by walking the style's children in document order, so the symbol lands
    /// back on the side the file put it. Anything else ODF allows in there (a colour, a negative-value
    /// <c>style:map</c>) isn't part of the code and is skipped.
    /// </summary>
    private static string CurrencyCode(XElement style)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in style.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "currency-symbol":
                case "text":
                    sb.Append(child.Value);
                    break;
                case "number":
                    sb.Append(NumberCore(child));
                    break;
            }
        }

        // A currency style with no symbol is just a number; keep the code honest rather than
        // classifying it as money on the strength of the element name alone.
        return sb.Length > 0 ? sb.ToString() : NumberCode(style);
    }

    private static string PercentCode(XElement style) => NumberCore(style.Element(Number + "number")) + "%";

    private static string DateCode(XElement style)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in style.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "year":
                    sb.Append(child.Attribute(Number + "style")?.Value == "long" ? "yyyy" : "yy");
                    break;
                case "month":
                    // A textual month is a different token, not a longer one: mmmm is "January" where
                    // mm is "01". Reading both back as m/mm turned every month name into a number.
                    bool textual = child.Attribute(Number + "textual")?.Value == "true";
                    bool longForm = child.Attribute(Number + "style")?.Value == "long";
                    sb.Append(textual ? (longForm ? "mmmm" : "mmm") : longForm ? "mm" : "m");
                    break;
                case "day":
                    sb.Append(child.Attribute(Number + "style")?.Value == "long" ? "dd" : "d");
                    break;
                case "text":
                    sb.Append(child.Value);
                    break;
            }
        }

        return sb.Length > 0 ? sb.ToString() : "yyyy-mm-dd";
    }

    private static int ReadDecimals(XElement? number)
    {
        var value = number?.Attribute(Number + "decimal-places")?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) ? d : 0;
    }
}
