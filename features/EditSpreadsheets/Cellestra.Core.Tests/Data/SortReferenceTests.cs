using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Data;

public class SortReferenceTests
{
    [Fact]
    public void Per_row_formula_references_follow_the_sorted_row()
    {
        var sheet = new Worksheet("S");
        // Header, then 3 data rows: column A = sort key, B = value, C = "=B?*2".
        sheet.SetValue(0, 0, CellValue.Text("Key"));
        sheet.SetValue(0, 1, CellValue.Text("Value"));
        sheet.SetValue(0, 2, CellValue.Text("Doubled"));

        sheet.SetValue(1, 0, CellValue.Number(3));
        sheet.SetValue(1, 1, CellValue.Number(30));
        sheet.SetFormula(1, 2, "B2*2", CellValue.Empty);
        sheet.SetValue(2, 0, CellValue.Number(1));
        sheet.SetValue(2, 1, CellValue.Number(10));
        sheet.SetFormula(2, 2, "B3*2", CellValue.Empty);
        sheet.SetValue(3, 0, CellValue.Number(2));
        sheet.SetValue(3, 1, CellValue.Number(20));
        sheet.SetFormula(3, 2, "B4*2", CellValue.Empty);

        SortEngine.Sort(sheet, CellRange.Parse("A1:C4"), new SortSpec([new SortLevel(0, true)]));

        // After sort the rows are Key 1,2,3; each Doubled formula still references its own row's B.
        Assert.Equal(1, sheet.GetValue(1, 0).AsNumber);
        Assert.Equal("B2*2", sheet.Find(1, 2)!.Formula); // was row 3 (B3*2) → moved up to row 2

        Assert.Equal(2, sheet.GetValue(2, 0).AsNumber);
        Assert.Equal("B3*2", sheet.Find(2, 2)!.Formula);

        Assert.Equal(3, sheet.GetValue(3, 0).AsNumber);
        Assert.Equal("B4*2", sheet.Find(3, 2)!.Formula); // was row 1 (B2*2) → moved down to row 4
    }

    [Fact]
    public void Absolute_and_out_of_range_references_are_unchanged()
    {
        var sheet = new Worksheet("S");
        sheet.SetValue(0, 0, CellValue.Number(2));
        sheet.SetValue(1, 0, CellValue.Number(1));
        // Reference to $A$10 (absolute, outside the sorted rows) must not move.
        sheet.SetFormula(0, 1, "A1+$A$10", CellValue.Empty);
        sheet.SetFormula(1, 1, "A2+$A$10", CellValue.Empty);

        SortEngine.Sort(sheet, CellRange.Parse("A1:B2"), new SortSpec([new SortLevel(0, true)], HasHeader: false));

        // Row with key 1 moved to top; its relative A2 became A1, absolute $A$10 stayed.
        Assert.Equal(1, sheet.GetValue(0, 0).AsNumber);
        Assert.Equal("A1+$A$10", sheet.Find(0, 1)!.Formula);
    }
}
