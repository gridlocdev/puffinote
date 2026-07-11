using System.Globalization;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;

namespace Cellestra.Core.Editing;

/// <summary>
/// Interprets what a user typed into a cell: a formula (leading <c>=</c>), a boolean, a number
/// (parsed in the active locale), a date (locale-aware, stored as a serial with a date format),
/// or text. This is the single place raw input becomes a value or formula (PRD §5 "Edit a value").
/// </summary>
public static class InputParser
{
    /// <param name="SuggestedFormat">
    /// A number format the caller should apply when the cell is still General — set when a typed
    /// date was coerced to a serial, so it displays as a date rather than a raw number.
    /// </param>
    public readonly record struct ParsedInput(string? Formula, CellValue Value, NumberFormat? SuggestedFormat = null)
    {
        public bool IsFormula => Formula is not null;
    }

    public static ParsedInput Parse(string text, CultureInfo culture)
    {
        if (text is null || text.Length == 0)
        {
            return new ParsedInput(null, CellValue.Empty);
        }

        // A leading apostrophe forces the rest to be stored as literal text, even when it looks
        // like a number, boolean, or formula ('007, '=notaformula). The apostrophe is an input
        // flag, not part of the stored value, and is never displayed (Excel/Calc convention).
        if (text[0] == '\'')
        {
            return new ParsedInput(null, CellValue.Text(text[1..]));
        }

        if (text[0] == '=' && text.Length > 1)
        {
            return new ParsedInput(text[1..], CellValue.Empty);
        }

        if (string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedInput(null, CellValue.Boolean(true));
        }

        if (string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedInput(null, CellValue.Boolean(false));
        }

        if (double.TryParse(text, NumberStyles.Any, culture, out double number))
        {
            return new ParsedInput(null, CellValue.Number(number));
        }

        if (TryParseDate(text, culture, out double serial, out NumberFormat dateFormat))
        {
            return new ParsedInput(null, CellValue.Number(serial), dateFormat);
        }

        return new ParsedInput(null, CellValue.Text(text));
    }

    /// <summary>
    /// Recognizes a typed date and converts it to an ODF serial. Bounded deliberately to
    /// slash dates (<c>3/4/2026</c>, locale-aware day/month order per PRD §8.3) and ISO dates
    /// (<c>2026-06-07</c>) so month names and arbitrary hyphenated tokens aren't silently coerced.
    /// Bare times are left as text for now — the format mapper treats <c>mm</c> as month, so a
    /// minutes-bearing time code would mis-render.
    /// </summary>
    private static bool TryParseDate(string text, CultureInfo culture, out double serial, out NumberFormat format)
    {
        serial = 0;
        format = NumberFormat.General;
        string trimmed = text.Trim();
        const DateTimeStyles styles = DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AllowWhiteSpaces;

        if (IsIsoDate(trimmed) &&
            DateTime.TryParseExact(trimmed, ["yyyy-M-d", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, styles, out var isoDate))
        {
            serial = DateConversion.ToSerial(isoDate.Date);
            format = new NumberFormat("YYYY-MM-DD");
            return true;
        }

        if (trimmed.IndexOf('/') >= 0 &&
            DateTime.TryParse(trimmed, TwoDigitYearCulture(culture), styles, out var date) &&
            date.TimeOfDay == TimeSpan.Zero)
        {
            serial = DateConversion.ToSerial(date.Date);
            format = new NumberFormat(DateCodeFromCulture(culture));
            return true;
        }

        return false;
    }

    /// <summary>
    /// The active locale, adjusted so a two-digit year resolves on LibreOffice Calc's default
    /// 1930–2029 window (so <c>3/4/30</c> is 1930, not .NET's default 2030). Cloned because the
    /// calendar's <see cref="System.Globalization.Calendar.TwoDigitYearMax"/> is per-instance.
    /// </summary>
    private static CultureInfo TwoDigitYearCulture(CultureInfo culture)
    {
        var adjusted = (CultureInfo)culture.Clone();
        adjusted.Calendar.TwoDigitYearMax = 2029;
        return adjusted;
    }

    private static bool IsIsoDate(string text)
    {
        // yyyy-m-d, allowing 1- or 2-digit month/day. No regex dependency.
        int firstDash = text.IndexOf('-');
        int lastDash = text.LastIndexOf('-');
        if (firstDash != 4 || lastDash == firstDash)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The active locale's short-date pattern as an Excel/ODF code (lowercase month).</summary>
    private static string DateCodeFromCulture(CultureInfo culture) =>
        culture.DateTimeFormat.ShortDatePattern.Replace('M', 'm');
}
