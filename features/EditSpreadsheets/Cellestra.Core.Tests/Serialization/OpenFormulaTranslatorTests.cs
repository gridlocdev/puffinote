using Cellestra.Core.Serialization.Ods;
using Xunit;

namespace Cellestra.Core.Tests.Serialization;

public class OpenFormulaTranslatorTests
{
    [Theory]
    [InlineData("SUM(A1:A3)", "of:=SUM([.A1:.A3])")]
    [InlineData("A1+B2", "of:=[.A1]+[.B2]")]
    [InlineData("IF(A1>0, B1, C1)", "of:=IF([.A1]>0;[.B1];[.C1])")]
    [InlineData("$A$1*2", "of:=[.$A$1]*2")]
    [InlineData("Sheet2.A1", "of:=[Sheet2.A1]")]
    [InlineData("\"a;b\"", "of:=\"a;b\"")]
    public void To_open_formula(string internalFormula, string expected)
    {
        Assert.Equal(expected, OpenFormulaTranslator.ToOpenFormula(internalFormula));
    }

    [Theory]
    [InlineData("of:=SUM([.A1:.A3])", "SUM(A1:A3)")]
    [InlineData("of:=[.A1]+[.B2]", "A1+B2")]
    [InlineData("of:=IF([.A1]>0;[.B1];[.C1])", "IF(A1>0, B1, C1)")]
    [InlineData("of:=[.$A$1]*2", "$A$1*2")]
    [InlineData("of:=['My Sheet'.A1]", "'My Sheet'.A1")]
    public void From_open_formula(string openFormula, string expected)
    {
        Assert.Equal(expected, OpenFormulaTranslator.FromOpenFormula(openFormula));
    }

    [Theory]
    [InlineData("SUM(A1:A3)")]
    [InlineData("IF(A1>0, A1*2, 0)")]
    [InlineData("$A$1+B$2-C3")]
    [InlineData("Sheet2.A1+A1")]
    [InlineData("\"x;y\"&A1")]
    public void Round_trips_through_open_formula(string internalFormula)
    {
        string odf = OpenFormulaTranslator.ToOpenFormula(internalFormula);
        Assert.Equal(internalFormula, OpenFormulaTranslator.FromOpenFormula(odf));
    }
}
