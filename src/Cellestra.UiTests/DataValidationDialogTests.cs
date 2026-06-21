using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Model;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Data Validation dialog fixes (PRD §8.13): the "And" operand re-enables when the operator is
/// between/not-between, and a Date rule accepts a typed date, coercing it to an ODF serial.
/// </summary>
public class DataValidationDialogTests
{
    private static readonly CellRange Range = CellRange.Parse("A1:A3");

    [AvaloniaFact]
    public void And_operand_enables_only_for_between_operators()
    {
        var dialog = new DataValidationDialog(Range);

        Assert.True(dialog.Operand2EnabledForTest(ValidationOperator.Between));
        Assert.True(dialog.Operand2EnabledForTest(ValidationOperator.NotBetween));
        Assert.False(dialog.Operand2EnabledForTest(ValidationOperator.GreaterThan));

        // The bug: switching back to "between" must re-enable it (the operator combo now drives this).
        Assert.True(dialog.Operand2EnabledForTest(ValidationOperator.Between));
    }

    [AvaloniaFact]
    public void Date_rule_parses_a_typed_date_into_a_serial()
    {
        var dialog = new DataValidationDialog(Range);

        var rule = dialog.BuildForTest(ValidationKind.Date, ValidationOperator.GreaterOrEqual, "2026-01-01");

        Assert.NotNull(rule);
        Assert.Equal(DateConversion.DateToSerial(2026, 1, 1), rule!.Operand1, 6);
    }

    [AvaloniaFact]
    public void Numeric_rule_still_parses_a_plain_number()
    {
        var dialog = new DataValidationDialog(Range);

        var rule = dialog.BuildForTest(ValidationKind.WholeNumber, ValidationOperator.Between, "1", "10");

        Assert.NotNull(rule);
        Assert.Equal(1, rule!.Operand1);
        Assert.Equal(10, rule.Operand2);
    }

    [AvaloniaFact]
    public void List_rule_uses_literal_values_when_no_range_is_given()
    {
        var dialog = new DataValidationDialog(Range);

        var rule = dialog.BuildListForTest("Red, Green, Blue");

        Assert.NotNull(rule);
        Assert.Equal(ValidationKind.List, rule!.Kind);
        Assert.Null(rule.ListSource);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, rule.ListValues);
    }

    [AvaloniaFact]
    public void List_rule_draws_from_a_cell_range_when_one_is_supplied()
    {
        var dialog = new DataValidationDialog(Range);

        // A range source wins over any literal values typed alongside it.
        var rule = dialog.BuildListForTest("ignored", "Lists.A1:A20");

        Assert.NotNull(rule);
        Assert.Equal(ValidationKind.List, rule!.Kind);
        Assert.Null(rule.ListValues);
        Assert.Equal(CellRange.Parse("Lists.A1:A20"), rule.ListSource);
    }
}
