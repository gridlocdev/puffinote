using Cellestra.Core.Formulas.References;
using Xunit;

namespace Cellestra.Core.Tests.Formulas;

public class ReferenceCyclerTests
{
    [Theory]
    [InlineData("=A1", "=$A$1")]
    [InlineData("=$A$1", "=A$1")]
    [InlineData("=A$1", "=$A1")]
    [InlineData("=$A1", "=A1")]
    public void Cycles_through_the_four_forms(string text, string expected)
    {
        // Caret at the end (just after typing the reference).
        var (result, _) = ReferenceCycler.Cycle(text, text.Length);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Cycles_the_reference_under_the_caret_within_a_larger_formula()
    {
        // =A1+B2 with the caret right after B2.
        var (result, caret) = ReferenceCycler.Cycle("=A1+B2", 6);
        Assert.Equal("=A1+$B$2", result);
        Assert.Equal(8, caret); // caret follows the grown reference
    }

    [Fact]
    public void Leaves_function_names_alone()
    {
        var (result, _) = ReferenceCycler.Cycle("=SUM(", 4); // caret inside "SUM"
        Assert.Equal("=SUM(", result);
    }

    [Fact]
    public void Multi_letter_columns_and_rows_cycle()
    {
        var (result, _) = ReferenceCycler.Cycle("=AB12", 5);
        Assert.Equal("=$AB$12", result);
    }
}
