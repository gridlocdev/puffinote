using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Formulas;

public class FunctionsTests
{
    private static Action<Worksheet> Column(int column, params double[] values) => sheet =>
    {
        for (int i = 0; i < values.Length; i++)
        {
            sheet.SetValue(i, column, CellValue.Number(values[i]));
        }
    };

    // ---- Math / statistics ------------------------------------------------

    [Theory]
    [InlineData("SUM(1,2,3)", 6)]
    [InlineData("PRODUCT(2,3,4)", 24)]
    [InlineData("AVERAGE(2,4,6)", 4)]
    [InlineData("MIN(5,2,8)", 2)]
    [InlineData("MAX(5,2,8)", 8)]
    [InlineData("ABS(-5)", 5)]
    [InlineData("INT(3.7)", 3)]
    [InlineData("INT(-3.2)", -4)]
    [InlineData("MOD(10,3)", 1)]
    [InlineData("MOD(-3,2)", 1)]
    [InlineData("ROUND(3.14159,2)", 3.14)]
    [InlineData("ROUND(2.5,0)", 3)]
    [InlineData("ROUND(-2.5,0)", -3)]
    [InlineData("ROUNDUP(3.1,0)", 4)]
    [InlineData("ROUNDDOWN(3.9,0)", 3)]
    [InlineData("POWER(2,10)", 1024)]
    [InlineData("SQRT(16)", 4)]
    public void Math_scalars(string formula, double expected)
    {
        Assert.Equal(expected, FormulaHarness.Number(formula), 10);
    }

    [Theory]
    // A value typed directly as an argument is coerced like arithmetic — booleans become 1/0 and
    // numeric strings parse — unlike values pulled from a range (Sum_over_range_ignores_text).
    [InlineData("SUM(TRUE,FALSE,5)", 6)]
    [InlineData("AVERAGE(TRUE,FALSE)", 0.5)]
    [InlineData("MAX(TRUE,0.5)", 1)]
    [InlineData("COUNT(\"x\",1,TRUE)", 2)]
    [InlineData("SUM(1,\"2\")", 3)]
    public void Direct_arguments_coerce_booleans_and_numeric_text(string formula, double expected)
    {
        Assert.Equal(expected, FormulaHarness.Number(formula), 10);
    }

    [Theory]
    // A direct non-numeric / non-logical argument is an error (a range would ignore it instead).
    [InlineData("SUM(1,\"x\")")]
    [InlineData("AND(1,\"x\")")]
    public void Direct_non_value_arguments_error(string formula)
    {
        Assert.Equal("#VALUE!", FormulaHarness.ErrorToken(formula));
    }

    [Theory]
    // Number→text coercion keeps fixed-point across Calc's wide band; G15 wrongly used scientific.
    [InlineData("0.000001&\"\"", "0.000001")]
    [InlineData("100000000000000000&\"\"", "100000000000000000")]
    public void Number_to_text_avoids_scientific_notation(string formula, string expected)
    {
        Assert.Equal(expected, FormulaHarness.Text(formula));
    }

    [Fact]
    public void Sum_over_range_ignores_text()
    {
        double result = FormulaHarness.Number("SUM(A1:A3)", s =>
        {
            s.SetValue(0, 0, CellValue.Number(1));
            s.SetValue(1, 0, CellValue.Text("ignored"));
            s.SetValue(2, 0, CellValue.Number(3));
        });

        Assert.Equal(4, result);
    }

    [Fact]
    public void Count_and_counta()
    {
        static void Setup(Worksheet s)
        {
            s.SetValue(0, 0, CellValue.Number(1));
            s.SetValue(1, 0, CellValue.Text("x"));
            s.SetValue(2, 0, CellValue.Number(3));
        }

        Assert.Equal(2, FormulaHarness.Number("COUNT(A1:A3)", Setup));
        Assert.Equal(3, FormulaHarness.Number("COUNTA(A1:A3)", Setup));
    }

    [Fact]
    public void Average_of_no_numbers_is_div_zero()
    {
        Assert.Equal("#DIV/0!", FormulaHarness.ErrorToken("AVERAGE(A1:A3)"));
    }

    [Fact]
    public void Sqrt_of_negative_is_error()
    {
        Assert.Equal("#VALUE!", FormulaHarness.ErrorToken("SQRT(-1)"));
    }

    [Fact]
    public void Sumif_and_countif()
    {
        Assert.Equal(12, FormulaHarness.Number("SUMIF(A1:A5,\">2\")", Column(0, 1, 2, 3, 4, 5)));
        Assert.Equal(3, FormulaHarness.Number("COUNTIF(A1:A5,\">2\")", Column(0, 1, 2, 3, 4, 5)));
    }

    [Fact]
    public void Sumif_with_separate_sum_range()
    {
        static void Setup(Worksheet s)
        {
            Column(0, 1, 2, 3, 4, 5)(s);
            Column(1, 10, 20, 30, 40, 50)(s);
        }

        Assert.Equal(120, FormulaHarness.Number("SUMIF(A1:A5,\">2\",B1:B5)", Setup));
    }

    [Fact]
    public void Sumif_reshapes_a_smaller_sum_range_to_the_criteria_range()
    {
        // sum_range B1:B3 is smaller than the criteria range A1:A5; Excel/Calc anchor it at B1 and
        // reshape it to A1:A5's shape, so the matched rows 3,4,5 read B3,B4,B5 = 30+40+50 = 120.
        static void Setup(Worksheet s)
        {
            Column(0, 1, 2, 3, 4, 5)(s);
            Column(1, 10, 20, 30, 40, 50)(s);
        }

        Assert.Equal(120, FormulaHarness.Number("SUMIF(A1:A5,\">2\",B1:B3)", Setup));
    }

    [Fact]
    public void Sumif_reshapes_a_single_cell_sum_range()
    {
        // A single-cell sum_range B1 is anchored and reshaped to A1:A5; matched rows 3,4,5 read
        // B3,B4,B5 = 30+40+50 = 120 (it must not be clamped to B1 nor yield #REF!).
        static void Setup(Worksheet s)
        {
            Column(0, 1, 2, 3, 4, 5)(s);
            Column(1, 10, 20, 30, 40, 50)(s);
        }

        Assert.Equal(120, FormulaHarness.Number("SUMIF(A1:A5,\">2\",B1)", Setup));
    }

    [Fact]
    public void Sumif_reshapes_an_offset_sum_range()
    {
        // sum_range C2:C6 is offset from the criteria range; anchored at C2, matched rows 3,4,5
        // (offsets 2,3,4) read C4,C5,C6 = 300+400+500 = 1200.
        static void Setup(Worksheet s)
        {
            Column(0, 1, 2, 3, 4, 5)(s);
            for (int i = 0; i < 5; i++)
            {
                s.SetValue(1 + i, 2, CellValue.Number((i + 1) * 100)); // C2..C6 = 100..500
            }
        }

        Assert.Equal(1200, FormulaHarness.Number("SUMIF(A1:A5,\">2\",C2:C6)", Setup));
    }

    // ---- Logical ----------------------------------------------------------

    [Theory]
    [InlineData("IF(1>0,10,20)", 10)]
    [InlineData("IF(1<0,10,20)", 20)]
    [InlineData("IFERROR(1/0,42)", 42)]
    [InlineData("IFERROR(7,42)", 7)]
    public void Logical_numbers(string formula, double expected)
    {
        Assert.Equal(expected, FormulaHarness.Number(formula), 10);
    }

    [Theory]
    [InlineData("AND(TRUE,TRUE)", true)]
    [InlineData("AND(TRUE,FALSE)", false)]
    [InlineData("OR(FALSE,TRUE)", true)]
    [InlineData("OR(FALSE,FALSE)", false)]
    [InlineData("NOT(TRUE)", false)]
    [InlineData("TRUE()", true)]
    [InlineData("FALSE()", false)]
    public void Logical_booleans(string formula, bool expected)
    {
        Assert.Equal(expected, FormulaHarness.Eval(formula).AsBoolean);
    }

    // ---- Text -------------------------------------------------------------

    [Theory]
    [InlineData("LEFT(\"Cellestra\",4)", "Cell")]
    [InlineData("RIGHT(\"Cellestra\",3)", "tra")]
    [InlineData("MID(\"Cellestra\",2,4)", "elle")]
    [InlineData("LOWER(\"ABC\")", "abc")]
    [InlineData("UPPER(\"abc\")", "ABC")]
    [InlineData("TRIM(\"  a  b \")", "a b")]
    [InlineData("SUBSTITUTE(\"a-b-c\",\"-\",\"+\")", "a+b+c")]
    [InlineData("SUBSTITUTE(\"a-b-c\",\"-\",\"+\",2)", "a-b+c")]
    [InlineData("CONCAT(\"a\",\"b\",\"c\")", "abc")]
    [InlineData("TEXT(0.25,\"0.0%\")", "25.0%")]
    public void Text_functions(string formula, string expected)
    {
        Assert.Equal(expected, FormulaHarness.Text(formula));
    }

    [Fact]
    public void Len_and_value()
    {
        Assert.Equal(9, FormulaHarness.Number("LEN(\"Cellestra\")"));
        Assert.Equal(123, FormulaHarness.Number("VALUE(\"123\")"));
    }

    [Theory]
    [InlineData("VALUE(\"50%\")", 0.5)]   // a trailing percent divides by 100
    [InlineData("VALUE(\"100%\")", 1.0)]
    [InlineData("VALUE(\"$100\")", 100)]  // a leading currency symbol is stripped
    [InlineData("VALUE(\"$1.5\")", 1.5)]
    public void Value_parses_percent_and_currency(string formula, double expected)
    {
        Assert.Equal(expected, FormulaHarness.Number(formula), 10);
    }

    // ---- Lookup -----------------------------------------------------------

    [Fact]
    public void Match_exact_and_approximate()
    {
        var setup = Column(0, 10, 20, 30, 40, 50);
        Assert.Equal(3, FormulaHarness.Number("MATCH(30,A1:A5,0)", setup));
        Assert.Equal(2, FormulaHarness.Number("MATCH(25,A1:A5,1)", setup));
    }

    [Fact]
    public void Match_not_found_is_na()
    {
        Assert.Equal("#N/A", FormulaHarness.ErrorToken("MATCH(99,A1:A5,0)", Column(0, 10, 20, 30, 40, 50)));
    }

    [Fact]
    public void Vlookup_exact()
    {
        static void Setup(Worksheet s)
        {
            s.SetValue(0, 0, CellValue.Number(1));
            s.SetValue(0, 1, CellValue.Text("one"));
            s.SetValue(1, 0, CellValue.Number(2));
            s.SetValue(1, 1, CellValue.Text("two"));
            s.SetValue(2, 0, CellValue.Number(3));
            s.SetValue(2, 1, CellValue.Text("three"));
        }

        Assert.Equal("two", FormulaHarness.Text("VLOOKUP(2,A1:B3,2,FALSE)", Setup));
        Assert.Equal("three", FormulaHarness.Text("INDEX(A1:B3,3,2)", Setup));
        Assert.Equal("two", FormulaHarness.Text("HLOOKUP(\"two\",A1:B3,1)", s =>
        {
            // For HLOOKUP search the first row.
            s.SetValue(0, 0, CellValue.Text("one"));
            s.SetValue(0, 1, CellValue.Text("two"));
            s.SetValue(1, 0, CellValue.Number(1));
            s.SetValue(1, 1, CellValue.Number(2));
        }));
    }

    // ---- Date / time ------------------------------------------------------

    [Fact]
    public void Date_round_trips_through_components()
    {
        Assert.Equal(2026, FormulaHarness.Number("YEAR(DATE(2026,6,7))"));
        Assert.Equal(6, FormulaHarness.Number("MONTH(DATE(2026,6,7))"));
        Assert.Equal(7, FormulaHarness.Number("DAY(DATE(2026,6,7))"));
    }

    [Theory]
    [InlineData("WEEKDAY(DATE(2000,1,1),1)", 7)] // Saturday, Sunday=1 system
    [InlineData("WEEKDAY(DATE(2000,1,1),2)", 6)] // Monday=1 system
    [InlineData("WEEKDAY(DATE(2000,1,1),3)", 5)] // Monday=0 system
    public void Weekday_types(string formula, double expected)
    {
        Assert.Equal(expected, FormulaHarness.Number(formula));
    }

    [Fact]
    public void Networkdays_counts_weekdays()
    {
        // 2026-06-01 (Mon) … 2026-06-05 (Fri) = 5 working days.
        Assert.Equal(5, FormulaHarness.Number("NETWORKDAYS(DATE(2026,6,1),DATE(2026,6,5))"));
        // Spanning the weekend to Mon 2026-06-08 adds one more working day.
        Assert.Equal(6, FormulaHarness.Number("NETWORKDAYS(DATE(2026,6,1),DATE(2026,6,8))"));
    }

    [Fact]
    public void Today_uses_the_injected_clock()
    {
        double today = FormulaHarness.Number("TODAY()", now: new DateTime(2026, 6, 7));
        double date = FormulaHarness.Number("DATE(2026,6,7)");
        Assert.Equal(date, today);
    }

    [Fact]
    public void Library_has_the_full_v1_set()
    {
        var registry = Cellestra.Core.Formulas.Functions.FunctionRegistry.CreateDefault();
        string[] expected =
        [
            "SUM", "AVERAGE", "MIN", "MAX", "COUNT", "COUNTA", "PRODUCT", "ABS", "INT", "MOD",
            "ROUND", "ROUNDUP", "ROUNDDOWN", "POWER", "SQRT", "SUMIF", "COUNTIF",
            "IF", "IFERROR", "AND", "OR", "NOT", "TRUE", "FALSE",
            "LEFT", "RIGHT", "MID", "LEN", "LOWER", "UPPER", "TRIM", "SUBSTITUTE", "TEXT", "VALUE", "CONCAT",
            "VLOOKUP", "HLOOKUP", "INDEX", "MATCH",
            "TODAY", "NOW", "DATE", "YEAR", "MONTH", "DAY", "WEEKDAY", "NETWORKDAYS",
        ];

        foreach (string name in expected)
        {
            Assert.True(registry.Contains(name), $"Missing function {name}");
        }
    }
}
