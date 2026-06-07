using Cellestra.Core.Model;
using Xunit;

namespace Cellestra.Core.Tests.Model;

public class CellAddressTests
{
    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("B7", 1, 6)]
    [InlineData("Z1", 25, 0)]
    [InlineData("AA1", 26, 0)]
    [InlineData("AB10", 27, 9)]
    [InlineData("XFD1048576", 16383, 1048575)]
    public void Parse_reads_column_and_row(string text, int expectedColumn, int expectedRow)
    {
        var address = CellAddress.Parse(text);

        Assert.Equal(expectedColumn, address.Column);
        Assert.Equal(expectedRow, address.Row);
    }

    [Theory]
    [InlineData("$A$1", true, true)]
    [InlineData("$A1", true, false)]
    [InlineData("A$1", false, true)]
    [InlineData("A1", false, false)]
    public void Parse_reads_absolute_flags(string text, bool colAbs, bool rowAbs)
    {
        var address = CellAddress.Parse(text);

        Assert.Equal(colAbs, address.ColumnAbsolute);
        Assert.Equal(rowAbs, address.RowAbsolute);
    }

    [Theory]
    [InlineData("Sheet2.A1", "Sheet2")]
    [InlineData("'My Sheet'.B2", "My Sheet")]
    [InlineData("'O''Brien'.C3", "O'Brien")]
    public void Parse_reads_sheet_qualifier(string text, string expectedSheet)
    {
        var address = CellAddress.Parse(text);

        Assert.Equal(expectedSheet, address.Sheet);
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("$A$1")]
    [InlineData("Sheet2.B7")]
    [InlineData("'My Sheet'.B7")]
    public void ToString_round_trips_parse(string text)
    {
        Assert.Equal(text, CellAddress.Parse(text).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1A")]
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("A0")]
    [InlineData("A1B")]
    [InlineData("$")]
    public void TryParse_rejects_garbage(string text)
    {
        Assert.False(CellAddress.TryParse(text, out _));
    }

    [Fact]
    public void Shift_moves_relative_parts_but_not_absolute()
    {
        var address = CellAddress.Parse("$B$2");
        Assert.Equal(CellAddress.Parse("$B$2"), address.Shift(3, 4));

        var relative = CellAddress.Parse("B2");
        Assert.Equal(CellAddress.Parse("E6"), relative.Shift(3, 4));

        var mixed = CellAddress.Parse("$B2");
        Assert.Equal(CellAddress.Parse("$B6"), mixed.Shift(3, 4));
    }

    [Fact]
    public void Column_letters_round_trip()
    {
        for (int column = 0; column < 16384; column += 257)
        {
            string letters = SheetDimensions.ColumnToLetters(column);
            Assert.Equal(column, SheetDimensions.LettersToColumn(letters));
        }
    }
}
