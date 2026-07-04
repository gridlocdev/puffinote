using System.Globalization;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The unified Sort &amp; Filter dialog: it seeds from the sheet's live sort/filter, compiles its
/// ordered step list back into one multi-level <see cref="SortSpec"/> + one <see cref="FilterSpec"/>,
/// previews the surviving row count without mutating the sheet, and parses a filter cell as either a
/// value list or a condition.
/// </summary>
public class SortFilterDialogTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly CellRange TableRange = CellRange.Parse("A1:B5");

    private static Worksheet TaskSheet(FilterSpec? filter = null, SortSpec? sort = null)
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Status"));
        (string Name, string Status)[] rows = [("a", "Open"), ("b", "Done"), ("c", "Open"), ("d", "Done")];
        for (int i = 0; i < rows.Length; i++)
        {
            sheet.SetValue(i + 1, 0, CellValue.Text(rows[i].Name));
            sheet.SetValue(i + 1, 1, CellValue.Text(rows[i].Status));
        }

        // The dialog is scoped to a region's own state (#2); a table carries its filter/sort on itself.
        sheet.AddTable(new Table("T", TableRange, HasHeader: true) { Filter = filter, Sort = sort });
        return sheet;
    }

    [AvaloniaFact]
    public void Seeds_from_the_tables_own_sort_and_filter_and_compiles_them_back()
    {
        var sheet = TaskSheet(
            sort: new SortSpec([new SortLevel(0, Ascending: true)]),
            filter: new FilterSpec(new Dictionary<int, ColumnFilter> { [1] = new ColumnFilter { AllowedValues = new HashSet<string> { "Open" } } }));

        var dialog = new SortFilterDialog(sheet, TableRange, hasHeader: true, Inv);
        var plan = dialog.BuildPlanForTest();

        Assert.NotNull(plan.Sort);
        Assert.Equal([0], plan.Sort!.Levels.Select(l => l.Column));
        Assert.NotNull(plan.Filter);
        Assert.Contains(1, plan.Filter!.Columns.Keys);
        Assert.Equal(["Open"], plan.Filter.Columns[1].AllowedValues!.OrderBy(v => v));
    }

    [AvaloniaFact]
    public void Opens_with_no_steps_when_the_region_is_unsorted_and_unfiltered()
    {
        // A freshly created table must not show a phantom "Sort by the first column" step — applying it
        // would otherwise silently sort the data that wasn't sorted.
        var sheet = TaskSheet();

        var dialog = new SortFilterDialog(sheet, TableRange, hasHeader: true, Inv);
        var plan = dialog.BuildPlanForTest();

        Assert.Null(plan.Sort);
        Assert.Null(plan.Filter);
    }

    [AvaloniaFact]
    public void Does_not_seed_a_different_regions_filter()
    {
        // A sheet-level AutoFilter on a DIFFERENT range must not leak into a table's dialog (#2).
        var sheet = TaskSheet();
        sheet.ActiveFilter = CellRange.Parse("D1:D9");
        sheet.ActiveFilterSpec = new FilterSpec(
            new Dictionary<int, ColumnFilter> { [3] = new ColumnFilter { AllowedValues = new HashSet<string> { "x" } } });

        var dialog = new SortFilterDialog(sheet, TableRange, hasHeader: true, Inv);
        var plan = dialog.BuildPlanForTest();

        Assert.Null(plan.Filter); // the table has no filter; the other region's is irrelevant here
    }

    [AvaloniaFact]
    public void Step_reorder_and_remove_buttons_have_tooltips()
    {
        var sheet = TaskSheet(sort: new SortSpec([new SortLevel(0, Ascending: true)]));
        var dialog = new SortFilterDialog(sheet, TableRange, hasHeader: true, Inv);

        Assert.Equal(["Move step up"], dialog.StepButtonTooltipsForTest("▲"));
        Assert.Equal(["Move step down"], dialog.StepButtonTooltipsForTest("▼"));
        Assert.Equal(["Remove step"], dialog.StepButtonTooltipsForTest("✕"));
    }

    [AvaloniaFact]
    public void Preview_counts_surviving_rows_without_hiding_them()
    {
        var sheet = TaskSheet(
            filter: new FilterSpec(new Dictionary<int, ColumnFilter> { [1] = new ColumnFilter { AllowedValues = new HashSet<string> { "Open" } } }));

        var dialog = new SortFilterDialog(sheet, TableRange, hasHeader: true, Inv);

        Assert.Equal("Showing 2 of 4 rows", dialog.CountTextForTest);
        Assert.False(sheet.IsRowHidden(1)); // preview never mutates the sheet
        Assert.False(sheet.IsRowHidden(2));
    }

    [AvaloniaTheory]
    [InlineData(">100", false)]
    [InlineData("*north*", false)]
    [InlineData("Open, Done", true)]
    [InlineData("Single", true)]
    public void Parses_filter_text_as_condition_or_value_list(string text, bool isValueList)
    {
        var filter = SortFilterDialog.ParseFilterForTest(text);
        Assert.NotNull(filter);
        if (isValueList)
        {
            Assert.NotNull(filter!.AllowedValues);
            Assert.Null(filter.Condition);
        }
        else
        {
            Assert.Null(filter!.AllowedValues);
            Assert.Equal(text, filter.Condition);
        }
    }

    [AvaloniaFact]
    public void Blank_filter_text_is_no_filter()
    {
        Assert.Null(SortFilterDialog.ParseFilterForTest("   "));
    }
}
