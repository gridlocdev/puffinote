using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Xunit;

namespace Cellestra.Core.Tests.Formulas;

public class ReferenceAdjusterTests
{
    private static string Write(Expr expr) => FormulaWriter.Write(expr);

    private static string Copy(string formula, int colDelta, int rowDelta) =>
        Write(ReferenceAdjuster.ForCopy(Parser.Parse(formula), colDelta, rowDelta));

    [Theory]
    [InlineData("A1+B1", "B2+C2")]
    [InlineData("$A$1+B1", "$A$1+C2")]
    [InlineData("$A1+A$1", "$A2+B$1")]
    [InlineData("SUM(A1:A3)", "SUM(B2:B4)")]
    public void Copy_shifts_relative_keeps_absolute(string formula, string expected)
    {
        Assert.Equal(expected, Copy(formula, 1, 1));
    }

    [Fact]
    public void Insert_rows_pushes_later_references_down()
    {
        var adjusted = ReferenceAdjuster.ForInsertRows(Parser.Parse("A5"), "Sheet1", "Sheet1", atRow: 2, count: 2);
        Assert.Equal("A7", Write(adjusted));
    }

    [Fact]
    public void Insert_rows_ignores_other_sheets()
    {
        var adjusted = ReferenceAdjuster.ForInsertRows(Parser.Parse("Sheet2.A5"), "Sheet1", "Sheet1", atRow: 2, count: 2);
        Assert.Equal("Sheet2.A5", Write(adjusted));
    }

    [Fact]
    public void Delete_rows_breaks_reference_to_deleted_cell()
    {
        var adjusted = ReferenceAdjuster.ForDeleteRows(Parser.Parse("A3"), "Sheet1", "Sheet1", atRow: 2, count: 1);
        Assert.Equal("#REF!", Write(adjusted));
    }

    [Fact]
    public void Delete_rows_shrinks_a_spanning_range()
    {
        var adjusted = ReferenceAdjuster.ForDeleteRows(Parser.Parse("SUM(A1:A5)"), "Sheet1", "Sheet1", atRow: 1, count: 2);
        Assert.Equal("SUM(A1:A3)", Write(adjusted));
    }

    [Fact]
    public void Insert_columns_shifts_references_right()
    {
        var adjusted = ReferenceAdjuster.ForInsertColumns(Parser.Parse("C1"), "Sheet1", "Sheet1", atColumn: 1, count: 1);
        Assert.Equal("D1", Write(adjusted));
    }

    [Fact]
    public void Insert_inside_a_range_grows_it()
    {
        // SUM(A1:A5): inserting a row at the last covered row (index 4 = A5) grows the range.
        var adjusted = ReferenceAdjuster.ForInsertRows(Parser.Parse("SUM(A1:A5)"), "Sheet1", "Sheet1", atRow: 4, count: 1);
        Assert.Equal("SUM(A1:A6)", Write(adjusted));
    }

    [Fact]
    public void Insert_just_past_a_range_does_not_grow_it()
    {
        // Inserting at the row immediately after the range (index 5, one past A5) leaves it alone —
        // the boundary edge does not expand.
        var adjusted = ReferenceAdjuster.ForInsertRows(Parser.Parse("SUM(A1:A5)"), "Sheet1", "Sheet1", atRow: 5, count: 1);
        Assert.Equal("SUM(A1:A5)", Write(adjusted));
    }
}

public class FormulaWriterTests
{
    [Theory]
    [InlineData("1+2*3", "1+2*3")]
    [InlineData("(1+2)*3", "(1+2)*3")]
    [InlineData("2^3^2", "2^3^2")]
    [InlineData("1-(2-3)", "1-(2-3)")]
    [InlineData("1-2+3", "1-2+3")]
    [InlineData("-A1%", "-A1%")]
    [InlineData("-2^2", "-2^2")]        // unary minus looser than ^; no parens needed
    [InlineData("(-2)^2", "(-2)^2")]    // parens preserved — changes meaning
    [InlineData("2^-3", "2^(-3)")]      // sign on the exponent (writer parenthesizes uniformly)
    [InlineData("-2*3", "-2*3")]
    public void Round_trips_with_minimal_parens(string formula, string expected)
    {
        Assert.Equal(expected, FormulaWriter.Write(Parser.Parse(formula)));
    }

    [Fact]
    public void Writes_string_literals_with_doubled_quotes()
    {
        Assert.Equal("\"a\"\"b\"", FormulaWriter.Write(Parser.Parse("\"a\"\"b\"")));
    }
}
