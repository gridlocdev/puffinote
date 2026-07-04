using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Serialization;

/// <summary>
/// Round-trips ODF <c>table:content-validation</c> (bug 4 / PRD §8.13). A validation rule set on a
/// range must survive save → reopen with its kind, operator, operands, list values, and Stop/error
/// behavior intact, referenced from validated cells via <c>table:content-validation-name</c>.
/// </summary>
public class OdsDataValidationTests
{
    private static Workbook RoundTrip(Workbook workbook)
    {
        byte[] bytes = OdsWriter.WriteToBytes(workbook);
        using var stream = new MemoryStream(bytes);
        return OdsReader.Read(stream);
    }

    [Fact]
    public void Whole_number_between_rule_round_trips()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(5));

        sheet.AddValidation(new DataValidation(ValidationKind.WholeNumber, CellRange.Parse("A1:A3"))
        {
            Operator = ValidationOperator.Between,
            Operand1 = 1,
            Operand2 = 10,
            Response = ValidationResponse.Stop,
            InputMessage = "Enter 1 to 10",
            ErrorMessage = "Out of range",
        });

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);

        Assert.Equal(ValidationKind.WholeNumber, rule.Kind);
        Assert.Equal(ValidationOperator.Between, rule.Operator);
        Assert.Equal(1, rule.Operand1);
        Assert.Equal(10, rule.Operand2);
        Assert.Equal(CellRange.Parse("A1:A3"), rule.Target);
        Assert.Equal(ValidationResponse.Stop, rule.Response);
        Assert.Equal("Enter 1 to 10", rule.InputMessage);
        Assert.Equal("Out of range", rule.ErrorMessage);
    }

    [Fact]
    public void Decimal_relational_rule_round_trips()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.AddValidation(new DataValidation(ValidationKind.Decimal, CellRange.Parse("B2"))
        {
            Operator = ValidationOperator.GreaterOrEqual,
            Operand1 = 2.5,
            Response = ValidationResponse.Warning,
        });

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);

        Assert.Equal(ValidationKind.Decimal, rule.Kind);
        Assert.Equal(ValidationOperator.GreaterOrEqual, rule.Operator);
        Assert.Equal(2.5, rule.Operand1);
        Assert.Equal(CellRange.Parse("B2"), rule.Target);
        Assert.Equal(ValidationResponse.Warning, rule.Response);
    }

    [Fact]
    public void Text_length_between_rule_round_trips()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.AddValidation(new DataValidation(ValidationKind.TextLength, CellRange.Parse("A1"))
        {
            Operator = ValidationOperator.Between,
            Operand1 = 2,
            Operand2 = 8,
        });

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);

        Assert.Equal(ValidationKind.TextLength, rule.Kind);
        Assert.Equal(ValidationOperator.Between, rule.Operator);
        Assert.Equal(2, rule.Operand1);
        Assert.Equal(8, rule.Operand2);
    }

    [Fact]
    public void List_rule_with_literal_values_round_trips()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.AddValidation(new DataValidation(ValidationKind.List, CellRange.Parse("A1:A2"))
        {
            ListValues = new[] { "Red", "Green", "Blue" },
            Response = ValidationResponse.Information,
        });

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);

        Assert.Equal(ValidationKind.List, rule.Kind);
        Assert.NotNull(rule.ListValues);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, rule.ListValues!);
        Assert.Equal(ValidationResponse.Information, rule.Response);
    }

    [Fact]
    public void List_rule_with_quoted_value_round_trips()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.AddValidation(new DataValidation(ValidationKind.List, CellRange.Parse("A1"))
        {
            ListValues = new[] { "a;b", "say \"hi\"" },
        });

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);
        Assert.Equal(new[] { "a;b", "say \"hi\"" }, rule.ListValues!);
    }

    [Fact]
    public void List_rule_with_a_cell_range_source_round_trips()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.AddValidation(new DataValidation(ValidationKind.List, CellRange.Parse("A1:A3"))
        {
            ListSource = CellRange.Parse("E1:E8"),
            Response = ValidationResponse.Stop,
        });

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);

        Assert.Equal(ValidationKind.List, rule.Kind);
        Assert.Null(rule.ListValues);
        Assert.NotNull(rule.ListSource);
        Assert.Equal(4, rule.ListSource!.Value.Left); // column E → index 4
        Assert.Equal(0, rule.ListSource!.Value.Top); // row 1 → index 0
        Assert.Equal(7, rule.ListSource!.Value.Bottom); // row 8 → index 7
    }

    [Fact]
    public void No_validation_emits_no_block_and_reopens_clean()
    {
        var workbook = Workbook.CreateNew();
        workbook.ActiveSheet.SetValue(0, 0, CellValue.Number(1));

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        Assert.Empty(reopened.Validations);
    }

    [Fact]
    public void Validation_on_blank_cells_still_round_trips()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;

        // No cell values at all — the validated cells are blank and past any used range.
        sheet.AddValidation(new DataValidation(ValidationKind.List, CellRange.Parse("C5:C7"))
        {
            ListValues = new[] { "Yes", "No" },
        });

        var reopened = RoundTrip(workbook).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);
        Assert.Equal(CellRange.Parse("C5:C7"), rule.Target);
        Assert.Equal(new[] { "Yes", "No" }, rule.ListValues!);
    }
}
