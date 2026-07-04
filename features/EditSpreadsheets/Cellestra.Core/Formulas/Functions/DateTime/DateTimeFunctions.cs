using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions.DateTime;

/// <summary>The v1 date &amp; time functions (PRD §8.2). TODAY/NOW are volatile (recomputed each pass).</summary>
public static class DateTimeFunctions
{
    public static IEnumerable<IFunction> All()
    {
        yield return new DelegateFunction(
            H.Meta("TODAY", FunctionCategory.DateTime, "Returns today's date.", ArgumentKind.Number, "TODAY()"),
            (_, ev) => CellValue.Number(System.Math.Floor(DateConversion.ToSerial(ev.Context.Now.Date))));

        yield return new DelegateFunction(
            H.Meta("NOW", FunctionCategory.DateTime, "Returns the current date and time.", ArgumentKind.Number, "NOW()"),
            (_, ev) => CellValue.Number(DateConversion.ToSerial(ev.Context.Now)));

        yield return new DelegateFunction(
            H.Meta("DATE", FunctionCategory.DateTime, "Builds a date from year, month, and day.", ArgumentKind.Number,
                "DATE(2026, 6, 7)", H.Num("year"), H.Num("month"), H.Num("day")),
            (args, ev) =>
            {
                if (!H.TryInt(ev, args[0], out int year, out var ey))
                {
                    return ey;
                }

                if (!H.TryInt(ev, args[1], out int month, out var em))
                {
                    return em;
                }

                if (!H.TryInt(ev, args[2], out int day, out var ed))
                {
                    return ed;
                }

                try
                {
                    return CellValue.Number(DateConversion.DateToSerial(year, month, day));
                }
                catch (ArgumentOutOfRangeException)
                {
                    return CellValue.FromError(CellError.Value("DATE is out of the supported range."));
                }
            });

        yield return Component("YEAR", "Returns the year of a date.", "YEAR(A1)", d => d.Year);
        yield return Component("MONTH", "Returns the month (1–12) of a date.", "MONTH(A1)", d => d.Month);
        yield return Component("DAY", "Returns the day of the month (1–31) of a date.", "DAY(A1)", d => d.Day);

        yield return new DelegateFunction(
            H.Meta("WEEKDAY", FunctionCategory.DateTime, "Returns the day of the week of a date as a number.",
                ArgumentKind.Number, "WEEKDAY(A1, 2)", H.Num("date"), H.Num("type", required: false)),
            (args, ev) =>
            {
                if (!H.TryNumber(ev, args[0], out double serial, out var es))
                {
                    return es;
                }

                int type = 1;
                if (args.Count > 1 && !H.TryInt(ev, args[1], out type, out var et))
                {
                    return et;
                }

                int dow = (int)DateConversion.DateFromSerial(serial).DayOfWeek; // Sunday = 0 … Saturday = 6
                return type switch
                {
                    1 => CellValue.Number(dow + 1),          // Sunday = 1 … Saturday = 7
                    2 => CellValue.Number(((dow + 6) % 7) + 1), // Monday = 1 … Sunday = 7
                    3 => CellValue.Number((dow + 6) % 7),    // Monday = 0 … Sunday = 6
                    _ => CellValue.FromError(CellError.Value("WEEKDAY type must be 1, 2, or 3.")),
                };
            });

        yield return new DelegateFunction(
            H.Meta("NETWORKDAYS", FunctionCategory.DateTime, "Counts the working days (Mon–Fri) between two dates.",
                ArgumentKind.Number, "NETWORKDAYS(A1, A2, Holidays)",
                H.Num("start"), H.Num("end"), H.Rng("holidays", required: false)),
            (args, ev) =>
            {
                if (!H.TryNumber(ev, args[0], out double startSerial, out var es))
                {
                    return es;
                }

                if (!H.TryNumber(ev, args[1], out double endSerial, out var ee))
                {
                    return ee;
                }

                var holidays = new HashSet<int>();
                if (args.Count > 2)
                {
                    foreach (var value in ev.EnumerateValues(args[2]))
                    {
                        if (value.IsError)
                        {
                            return value;
                        }

                        if (value.IsNumber)
                        {
                            holidays.Add((int)System.Math.Floor(value.AsNumber));
                        }
                    }
                }

                int start = (int)System.Math.Floor(startSerial);
                int end = (int)System.Math.Floor(endSerial);
                int sign = 1;
                if (start > end)
                {
                    (start, end) = (end, start);
                    sign = -1;
                }

                int count = 0;
                for (int day = start; day <= end; day++)
                {
                    var dow = DateConversion.DateFromSerial(day).DayOfWeek;
                    if (dow is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    {
                        continue;
                    }

                    if (holidays.Contains(day))
                    {
                        continue;
                    }

                    count++;
                }

                return CellValue.Number(sign * count);
            });
    }

    private static IFunction Component(string name, string summary, string example, Func<System.DateTime, int> select) =>
        new DelegateFunction(
            H.Meta(name, FunctionCategory.DateTime, summary, ArgumentKind.Number, example, H.Num("date")),
            (args, ev) => H.TryNumber(ev, args[0], out double serial, out var error)
                ? CellValue.Number(select(DateConversion.DateFromSerial(serial)))
                : error);
}
