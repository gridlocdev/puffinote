using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Formulas;

public class EvaluatorTests
{
    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("2*3", 6)]
    [InlineData("10-4", 6)]
    [InlineData("10/4", 2.5)]
    [InlineData("2^10", 1024)]
    [InlineData("2^3^2", 64)]      // left-associative
    [InlineData("-5", -5)]
    [InlineData("50%", 0.5)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("\"5\"+3", 8)]      // text coerces in arithmetic
    public void Arithmetic(string formula, double expected)
    {
        Assert.Equal(expected, FormulaHarness.Number(formula), 10);
    }

    [Fact]
    public void Division_by_zero_is_an_error()
    {
        Assert.Equal("#DIV/0!", FormulaHarness.ErrorToken("10/0"));
    }

    [Theory]
    [InlineData("1<2", true)]
    [InlineData("2<=2", true)]
    [InlineData("3>4", false)]
    [InlineData("1=1", true)]
    [InlineData("2<>3", true)]
    [InlineData("\"a\"=\"A\"", true)]   // text equality is case-insensitive
    [InlineData("\"apple\"<\"banana\"", true)]
    public void Comparisons(string formula, bool expected)
    {
        Assert.Equal(expected, FormulaHarness.Eval(formula).AsBoolean);
    }

    [Fact]
    public void Concatenation()
    {
        Assert.Equal("ab", FormulaHarness.Text("\"a\"&\"b\""));
        Assert.Equal("12", FormulaHarness.Text("1&2"));
    }

    [Fact]
    public void Cell_reference_reads_value()
    {
        double result = FormulaHarness.Number("A1+A2", s =>
        {
            s.SetValue(0, 0, CellValue.Number(10));
            s.SetValue(1, 0, CellValue.Number(5));
        });

        Assert.Equal(15, result);
    }

    [Fact]
    public void Blank_cell_is_zero_in_arithmetic()
    {
        Assert.Equal(5, FormulaHarness.Number("A1+5"));
    }

    [Fact]
    public void Bare_range_in_scalar_position_is_value_error()
    {
        Assert.Equal("#VALUE!", FormulaHarness.ErrorToken("A1:A3+1"));
    }

    [Fact]
    public void Unknown_name_is_name_error()
    {
        Assert.Equal("#NAME?", FormulaHarness.ErrorToken("NotDefined"));
    }

    [Fact]
    public void Unknown_function_is_name_error()
    {
        Assert.Equal("#NAME?", FormulaHarness.ErrorToken("BOGUS(1)"));
    }

    [Fact]
    public void Errors_propagate_through_arithmetic()
    {
        Assert.Equal("#DIV/0!", FormulaHarness.ErrorToken("(1/0)+5"));
    }

    [Fact]
    public void Named_range_resolves_to_single_cell()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(0.2));
        workbook.AddNamedRange(new NamedRange("TaxRate", CellRange.Parse("A1")));

        var context = new Cellestra.Core.Formulas.Evaluation.WorkbookEvaluationContext(
            workbook, sheet.Name, System.Globalization.CultureInfo.InvariantCulture);
        var evaluator = new Cellestra.Core.Formulas.Evaluation.Evaluator(
            context, Cellestra.Core.Formulas.Functions.FunctionRegistry.CreateDefault());
        var value = evaluator.Evaluate(Cellestra.Core.Formulas.Parsing.Parser.Parse("TaxRate*100"));

        Assert.Equal(20, value.AsNumber, 10);
    }
}
