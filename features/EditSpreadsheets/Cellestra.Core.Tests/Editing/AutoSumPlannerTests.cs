using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class AutoSumPlannerTests
{
    private static Worksheet New() => Workbook.CreateNew().ActiveSheet;

    private static IReadOnlyList<AutoSumProposal> PlanAt(Worksheet sheet, string activeCell)
    {
        var address = CellAddress.Parse(activeCell);
        return AutoSumPlanner.Plan(sheet, new CellRange(address), address.Row, address.Column);
    }

    [Fact]
    public void Single_cell_sums_the_contiguous_run_above()
    {
        var sheet = New();
        sheet.SetValue(0, 0, CellValue.Number(1)); // A1
        sheet.SetValue(1, 0, CellValue.Number(2)); // A2
        sheet.SetValue(2, 0, CellValue.Number(3)); // A3
        // A4 is the active (empty) cell.

        var plan = PlanAt(sheet, "A4");

        var p = Assert.Single(plan);
        Assert.Equal(3, p.TargetRow);
        Assert.Equal(0, p.TargetColumn);
        Assert.Equal("=SUM(A1:A3)", p.Formula);
    }

    [Fact]
    public void Run_above_stops_at_the_first_gap()
    {
        var sheet = New();
        sheet.SetValue(0, 0, CellValue.Number(1)); // A1
        // A2 blank — breaks the run
        sheet.SetValue(2, 0, CellValue.Number(3)); // A3
        sheet.SetValue(3, 0, CellValue.Number(4)); // A4
        // A5 active

        var plan = PlanAt(sheet, "A5");

        Assert.Equal("=SUM(A3:A4)", Assert.Single(plan).Formula);
    }

    [Fact]
    public void Falls_back_to_the_run_to_the_left_when_nothing_above()
    {
        var sheet = New();
        sheet.SetValue(2, 0, CellValue.Number(1)); // A3
        sheet.SetValue(2, 1, CellValue.Number(2)); // B3
        sheet.SetValue(2, 2, CellValue.Number(3)); // C3
        // D3 active, nothing above it.

        var plan = PlanAt(sheet, "D3");

        Assert.Equal("=SUM(A3:C3)", Assert.Single(plan).Formula);
    }

    [Fact]
    public void Prefers_above_over_left_when_both_present()
    {
        var sheet = New();
        sheet.SetValue(0, 2, CellValue.Number(10)); // C1
        sheet.SetValue(1, 2, CellValue.Number(20)); // C2 — above C3
        sheet.SetValue(2, 0, CellValue.Number(1));  // A3
        sheet.SetValue(2, 1, CellValue.Number(2));  // B3 — left of C3
        // C3 active

        var plan = PlanAt(sheet, "C3");

        Assert.Equal("=SUM(C1:C2)", Assert.Single(plan).Formula); // above wins
    }

    [Fact]
    public void No_numbers_above_or_left_yields_no_proposal()
    {
        var sheet = New();
        var plan = PlanAt(sheet, "B2");
        Assert.Empty(plan);
    }

    [Fact]
    public void Multi_cell_selection_totals_each_column_into_the_row_below()
    {
        var sheet = New();
        // A1:B2 grid of numbers.
        sheet.SetValue(0, 0, CellValue.Number(1)); // A1
        sheet.SetValue(1, 0, CellValue.Number(2)); // A2
        sheet.SetValue(0, 1, CellValue.Number(3)); // B1
        sheet.SetValue(1, 1, CellValue.Number(4)); // B2

        var plan = AutoSumPlanner.Plan(sheet, CellRange.Parse("A1:B2"), 0, 0);

        Assert.Equal(2, plan.Count);
        Assert.Equal("=SUM(A1:A2)", plan[0].Formula);
        Assert.Equal(2, plan[0].TargetRow); // row 3 (below the selection)
        Assert.Equal(0, plan[0].TargetColumn);
        Assert.Equal("=SUM(B1:B2)", plan[1].Formula);
        Assert.Equal(1, plan[1].TargetColumn);
    }

    [Fact]
    public void Multi_cell_selection_skips_non_numeric_columns()
    {
        var sheet = New();
        sheet.SetValue(0, 0, CellValue.Text("Label")); // A1 — text column
        sheet.SetValue(1, 0, CellValue.Text("Total"));
        sheet.SetValue(0, 1, CellValue.Number(3)); // B1
        sheet.SetValue(1, 1, CellValue.Number(4)); // B2

        var plan = AutoSumPlanner.Plan(sheet, CellRange.Parse("A1:B2"), 0, 0);

        var p = Assert.Single(plan); // only the numeric column B
        Assert.Equal("=SUM(B1:B2)", p.Formula);
        Assert.Equal(1, p.TargetColumn);
    }
}
