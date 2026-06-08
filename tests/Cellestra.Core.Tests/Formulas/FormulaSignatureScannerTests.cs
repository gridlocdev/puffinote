using Cellestra.Core.Formulas.Functions;
using Xunit;

namespace Cellestra.Core.Tests.Formulas;

public class FormulaSignatureScannerTests
{
    // Marks the caret with '|'.
    private static FormulaCallSite? Find(string withCaret)
    {
        int caret = withCaret.IndexOf('|');
        string formula = withCaret.Replace("|", string.Empty);
        return FormulaSignatureScanner.Find(formula, caret);
    }

    [Fact]
    public void Caret_just_inside_the_paren_is_argument_zero()
    {
        var site = Find("=SUM(|");
        Assert.Equal("SUM", site!.FunctionName);
        Assert.Equal(0, site.ArgumentIndex);
    }

    [Fact]
    public void Commas_advance_the_argument_index()
    {
        var site = Find("=SUM(A1, B2, |");
        Assert.Equal("SUM", site!.FunctionName);
        Assert.Equal(2, site.ArgumentIndex);
    }

    [Fact]
    public void Nested_calls_report_the_innermost()
    {
        var site = Find("=SUM(A1, AVERAGE(B1, |");
        Assert.Equal("AVERAGE", site!.FunctionName);
        Assert.Equal(1, site.ArgumentIndex);
    }

    [Fact]
    public void After_a_closed_nested_call_reports_the_outer()
    {
        var site = Find("=SUM(A1, AVERAGE(B1, B2), |");
        Assert.Equal("SUM", site!.FunctionName);
        Assert.Equal(2, site.ArgumentIndex);
    }

    [Fact]
    public void Commas_inside_string_literals_are_ignored()
    {
        var site = Find("=IF(A1=\"a,b,c\", |");
        Assert.Equal("IF", site!.FunctionName);
        Assert.Equal(1, site.ArgumentIndex);
    }

    [Fact]
    public void Outside_any_call_returns_null()
    {
        Assert.Null(Find("=A1+|"));
        Assert.Null(Find("=SUM(A1)|")); // caret after the closing paren
    }

    [Fact]
    public void Grouping_parens_fall_through_to_the_enclosing_call()
    {
        var site = Find("=SUM((A1+B1)|");
        Assert.Equal("SUM", site!.FunctionName);
        Assert.Equal(0, site.ArgumentIndex);
    }
}
