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
        // DATE allows overflowing months/days to roll over (e.g. DATE(2020,13,1) = 2021-01-01).
        var date = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMonths(month - 1)
            .AddDays(day - 1);
        return ToSerial(date);
    }

    public static DateTime FromSerial(double serial) => Epoch.AddDays(serial);

    /// <summary>The whole-day part of a serial, truncated toward zero (drops the time fraction).</summary>
    public static DateTime DateFromSerial(double serial) => Epoch.AddDays(Math.Floor(serial));
}
