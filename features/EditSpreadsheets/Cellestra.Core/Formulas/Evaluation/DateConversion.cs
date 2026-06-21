namespace Cellestra.Core.Formulas.Evaluation;

/// <summary>
/// Date/time serial-number conversions using the ODF/Calc default epoch of
/// <b>1899-12-30</b> (serial 0). Whole days are integers; the time of day is the
/// fractional part. Matches how LibreOffice stores and computes dates.
/// </summary>
public static class DateConversion
{
    /// <summary>Serial 0. A date's serial is the number of days after this.</summary>
    public static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    public static double ToSerial(DateTime dateTime) => (dateTime - Epoch).TotalDays;

    public static double DateToSerial(int year, int month, int day)
    {
        // A two-digit year (0–99) resolves on LibreOffice Calc's default window 1930–2029
        // (00–29 → 2000–2029, 30–99 → 1930–1999), matching PRD §8.3, so DATE(30,1,1) = 1930.
        // Years 100–1899 are taken literally (LibreOffice does not shift those).
        if (year is >= 0 and <= 99)
        {
            year += year <= 29 ? 2000 : 1900;
        }

        // DATE allows overflowing months/days to roll over (e.g. DATE(2020,13,1) = 2021-01-01).
        var date = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMonths(month - 1)
            .AddDays(day - 1);
        return ToSerial(date);
    }

    public static DateTime FromSerial(double serial) => Epoch.AddDays(serial);

    /// <summary>The whole-day part of a serial, truncated toward zero (drops the time fraction).</summary>
    public static DateTime DateFromSerial(double serial) => Epoch.AddDays(Math.Floor(serial));

    /// <summary>
    /// Convert a serial to a <see cref="DateTime"/> without throwing. Returns false when the serial
    /// falls outside the representable range (years 1–9999) — or is NaN/infinite — so a date/time
    /// number format can fall back to a plain number instead of crashing (LibreOffice shows such
    /// out-of-range serials as numbers, not as dates). <paramref name="dateOnly"/> truncates the
    /// time-of-day fraction the way a pure date format does.
    /// </summary>
    public static bool TryToDateTime(double serial, bool dateOnly, out DateTime result)
    {
        result = default;
        if (double.IsNaN(serial) || double.IsInfinity(serial))
        {
            return false;
        }

        // DateTime spans 0001-01-01 .. 9999-12-31; express those bounds as serials about the epoch
        // and reject anything beyond them before AddDays would throw.
        double days = dateOnly ? Math.Floor(serial) : serial;
        double minSerial = (DateTime.MinValue - Epoch).TotalDays;
        double maxSerial = (DateTime.MaxValue - Epoch).TotalDays;
        if (days < minSerial || days > maxSerial)
        {
            return false;
        }

        result = Epoch.AddDays(days);
        return true;
    }
}
