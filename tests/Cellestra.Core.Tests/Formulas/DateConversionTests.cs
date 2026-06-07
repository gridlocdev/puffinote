using Cellestra.Core.Formulas.Evaluation;
using Xunit;

namespace Cellestra.Core.Tests.Formulas;

/// <summary>
/// Locks in the ODF/Calc date-serial conventions (PRD §8): epoch 1899-12-30 and — unlike
/// the Excel 1900 system — <b>no phantom 29 Feb 1900</b> leap day, so naive serial math
/// stays aligned with LibreOffice.
/// </summary>
public class DateConversionTests
{
    [Fact]
    public void Epoch_is_serial_zero()
    {
        Assert.Equal(0, DateConversion.ToSerial(new System.DateTime(1899, 12, 30)), 9);
        Assert.Equal(1, DateConversion.ToSerial(new System.DateTime(1899, 12, 31)), 9);
    }

    [Fact]
    public void No_phantom_1900_leap_day()
    {
        // Excel's 1900 system carries a non-existent 29 Feb 1900 (serial 60). ODF does not:
        // 28 Feb 1900 and 1 Mar 1900 are exactly one day apart, and DATE(1900,2,29) rolls
        // over to 1 Mar rather than naming a phantom day.
        double feb28 = DateConversion.DateToSerial(1900, 2, 28);
        double mar1 = DateConversion.DateToSerial(1900, 3, 1);
        Assert.Equal(1, mar1 - feb28, 9);
        Assert.Equal(mar1, DateConversion.DateToSerial(1900, 2, 29), 9); // overflow rolls forward
    }

    [Fact]
    public void Serial_round_trips_with_time_fraction()
    {
        var noon = new System.DateTime(2026, 6, 7, 12, 0, 0);
        double serial = DateConversion.ToSerial(noon);
        Assert.Equal(0.5, serial - System.Math.Floor(serial), 9); // noon = half a day
        Assert.Equal(noon, DateConversion.FromSerial(serial));
    }
}
