using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Data;

public class StickyTableHeaderTests
{
    /// <summary>A table over A1:B10 with a header row.</summary>
    private static Worksheet Sheet(bool hasHeader = true)
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Text("Team"));
        sheet.SetValue(0, 1, CellValue.Text("Points"));
        sheet.AddTable(new Table("T", CellRange.Parse("A1:B10"), hasHeader));
        return sheet;
    }

    [Fact]
    public void Scrolled_into_the_body_the_header_row_labels_the_column()
    {
        Assert.Equal(0, StickyTableHeader.LabelRow(Sheet(), column: 1, topRow: 4));
    }

    [Fact]
    public void With_the_real_header_on_screen_there_is_nothing_to_repeat()
    {
        Assert.Null(StickyTableHeader.LabelRow(Sheet(), column: 1, topRow: 0));
    }

    [Fact]
    public void Scrolled_past_the_last_row_the_letters_come_back()
    {
        // The "and the opposite" half: leaving the table has to undo it, not leave a stale title up.
        Assert.Null(StickyTableHeader.LabelRow(Sheet(), column: 1, topRow: 10));
    }

    [Fact]
    public void A_column_outside_the_table_is_never_relabelled()
    {
        Assert.Null(StickyTableHeader.LabelRow(Sheet(), column: 5, topRow: 4));
    }

    [Fact]
    public void A_headerless_table_has_no_title_to_lend()
    {
        Assert.Null(StickyTableHeader.LabelRow(Sheet(hasHeader: false), column: 1, topRow: 4));
    }
}
