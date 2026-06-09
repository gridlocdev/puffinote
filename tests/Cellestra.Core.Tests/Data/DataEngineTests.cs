using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Data;

public class DataEngineTests
{
    private static Worksheet SheetWith(params (int Row, int Col, object Value)[] cells)
    {
        var sheet = new Worksheet("S");
        foreach (var (row, col, value) in cells)
        {
            sheet.SetValue(row, col, value switch
            {
                double d => CellValue.Number(d),
                int i => CellValue.Number(i),
                string s => CellValue.Text(s),
                _ => CellValue.Empty,
            });
        }

        return sheet;
    }

    [Fact]
    public void Sort_orders_data_rows_keeping_header()
    {
        var sheet = SheetWith(
            (0, 0, "Name"), (0, 1, "Score"),
            (1, 0, "Bev"), (1, 1, 87),
            (2, 0, "Ada"), (2, 1, 99),
            (3, 0, "Cy"), (3, 1, 50));

        SortEngine.Sort(sheet, CellRange.Parse("A1:B4"),
            new SortSpec(new[] { new SortLevel(0, Ascending: true) }));

        Assert.Equal("Name", sheet.GetValue(0, 0).AsText); // header stays
        Assert.Equal("Ada", sheet.GetValue(1, 0).AsText);
        Assert.Equal("Bev", sheet.GetValue(2, 0).AsText);
        Assert.Equal("Cy", sheet.GetValue(3, 0).AsText);
    }

    [Fact]
    public void Multi_level_sort_is_stable()
    {
        var sheet = SheetWith(
            (0, 0, "Group"), (0, 1, "N"),
            (1, 0, "A"), (1, 1, 2),
            (2, 0, "A"), (2, 1, 1),
            (3, 0, "B"), (3, 1, 1));

        SortEngine.Sort(sheet, CellRange.Parse("A1:B4"),
            new SortSpec(new[] { new SortLevel(0, true), new SortLevel(1, true) }));

        Assert.Equal(1, sheet.GetValue(1, 1).AsNumber); // A,1
        Assert.Equal(2, sheet.GetValue(2, 1).AsNumber); // A,2
        Assert.Equal("B", sheet.GetValue(3, 0).AsText);
    }

    [Fact]
    public void Filter_hides_non_matching_rows()
    {
        var sheet = SheetWith(
            (0, 0, "Score"),
            (1, 0, 10), (2, 0, 50), (3, 0, 90));

        var spec = new FilterSpec(new Dictionary<int, ColumnFilter>
        {
            [0] = new ColumnFilter { Condition = ">40" },
        });

        FilterEngine.Apply(sheet, CellRange.Parse("A1:A4"), spec, CultureInfo.InvariantCulture);

        Assert.True(sheet.IsRowHidden(1));   // 10 hidden
        Assert.False(sheet.IsRowHidden(2));  // 50 visible
        Assert.False(sheet.IsRowHidden(3));  // 90 visible

        FilterEngine.Clear(sheet, CellRange.Parse("A1:A4"));
        Assert.False(sheet.IsRowHidden(1));
    }

    [Fact]
    public void Sort_reapplies_the_active_filter_so_visibility_follows_the_data()
    {
        var sheet = SheetWith(
            (0, 0, "Score"),
            (1, 0, 90), (2, 0, 10), (3, 0, 50));

        var range = CellRange.Parse("A1:A4");
        var spec = new FilterSpec(new Dictionary<int, ColumnFilter>
        {
            [0] = new ColumnFilter { Condition = ">40" },
        });

        FilterEngine.Apply(sheet, range, spec, CultureInfo.InvariantCulture);
        sheet.ActiveFilter = range;
        sheet.ActiveFilterSpec = spec;

        // Before sort: 90 (row1) and 50 (row3) visible; 10 (row2) hidden.
        Assert.False(sheet.IsRowHidden(1));
        Assert.True(sheet.IsRowHidden(2));
        Assert.False(sheet.IsRowHidden(3));

        // Sort ascending — data becomes 10, 50, 90. Without re-applying the filter the Hidden flags
        // would stay pinned to the old indices and hide the wrong row.
        SortEngine.Sort(sheet, range, new SortSpec(new[] { new SortLevel(0, Ascending: true) }), CultureInfo.InvariantCulture);

        Assert.Equal(10, sheet.GetValue(1, 0).AsNumber);
        Assert.Equal(50, sheet.GetValue(2, 0).AsNumber);
        Assert.Equal(90, sheet.GetValue(3, 0).AsNumber);

        // After sort: 10 (row1) hidden; 50 (row2) and 90 (row3) visible — visibility tracks values.
        Assert.True(sheet.IsRowHidden(1));
        Assert.False(sheet.IsRowHidden(2));
        Assert.False(sheet.IsRowHidden(3));
    }

    [Fact]
    public void Remove_duplicates_keeps_first_occurrence()
    {
        var sheet = SheetWith(
            (0, 0, "x"), (1, 0, "a"), (2, 0, "b"), (3, 0, "a"), (4, 0, "b"), (5, 0, "c"));

        int removed = DuplicateRemover.Remove(sheet, CellRange.Parse("A1:A6"), new[] { 0 }, hasHeader: true);

        Assert.Equal(2, removed);
        Assert.Equal("a", sheet.GetValue(1, 0).AsText);
        Assert.Equal("b", sheet.GetValue(2, 0).AsText);
        Assert.Equal("c", sheet.GetValue(3, 0).AsText);
        Assert.True(sheet.GetValue(4, 0).IsEmpty);
    }

    [Fact]
    public void Text_to_columns_splits_on_delimiter()
    {
        var sheet = SheetWith((0, 0, "a,b,c"), (1, 0, "d,e"));

        TextToColumns.Split(sheet, CellRange.Parse("A1:A2"), ',');

        Assert.Equal("a", sheet.GetValue(0, 0).AsText);
        Assert.Equal("b", sheet.GetValue(0, 1).AsText);
        Assert.Equal("c", sheet.GetValue(0, 2).AsText);
        Assert.Equal("e", sheet.GetValue(1, 1).AsText);
    }

    [Fact]
    public void Table_auto_expands_below_and_right()
    {
        var table = new Table("Data", CellRange.Parse("A1:B3"));

        Assert.Equal(CellRange.Parse("A1:B4"), TableAutoExpand.ExpandFor(table, 3, 1)); // below
        Assert.Equal(CellRange.Parse("A1:C3"), TableAutoExpand.ExpandFor(table, 0, 2)); // right
        Assert.Null(TableAutoExpand.ExpandFor(table, 10, 10));                          // far away
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(15, false)]
    public void Validation_number_between(double input, bool valid)
    {
        var rule = new DataValidation(ValidationKind.WholeNumber, CellRange.Parse("A1"))
        {
            Operator = ValidationOperator.Between,
            Operand1 = 1,
            Operand2 = 10,
        };

        Assert.Equal(valid, Validator.IsValid(rule, CellValue.Number(input), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Validation_list_accepts_only_listed_values()
    {
        var rule = new DataValidation(ValidationKind.List, CellRange.Parse("A1"))
        {
            ListValues = new[] { "Red", "Green", "Blue" },
        };

        Assert.True(Validator.IsValid(rule, CellValue.Text("Green"), CultureInfo.InvariantCulture));
        Assert.False(Validator.IsValid(rule, CellValue.Text("Purple"), CultureInfo.InvariantCulture));
    }
}
