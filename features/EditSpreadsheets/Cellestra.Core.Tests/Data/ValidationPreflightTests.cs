using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Data;

/// <summary>Batch validation pre-flight for paste/fill (PRD §8.13).</summary>
public class ValidationPreflightTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static Worksheet SheetWithWholeNumberRule(CellRange target, int min, int max)
    {
        var sheet = new Worksheet("Sheet1");
        sheet.AddValidation(new DataValidation(ValidationKind.WholeNumber, target)
        {
            Operator = ValidationOperator.Between,
            Operand1 = min,
            Operand2 = max,
        });
        return sheet;
    }

    [Fact]
    public void Reports_cells_that_violate_a_stop_rule()
    {
        var sheet = SheetWithWholeNumberRule(CellRange.Parse("A1:A3"), 1, 10);
        var incoming = new (int, int, CellValue, bool)[]
        {
            (0, 0, CellValue.Number(5), false),    // valid
            (1, 0, CellValue.Number(99), false),   // out of range → violation
            (2, 0, CellValue.Number(3), false),    // valid
        };

        var violations = ValidationPreflight.FindStopViolations(sheet, incoming, Culture);

        Assert.Equal(new[] { (1, 0) }, violations);
    }

    [Fact]
    public void Skips_formula_targets()
    {
        var sheet = SheetWithWholeNumberRule(CellRange.Parse("A1:A3"), 1, 10);
        var incoming = new (int, int, CellValue, bool)[] { (0, 0, CellValue.Number(999), true) };

        Assert.Empty(ValidationPreflight.FindStopViolations(sheet, incoming, Culture));
    }

    [Fact]
    public void Empty_values_pass()
    {
        var sheet = SheetWithWholeNumberRule(CellRange.Parse("A1:A3"), 1, 10);
        var incoming = new (int, int, CellValue, bool)[] { (0, 0, CellValue.Empty, false) };

        Assert.Empty(ValidationPreflight.FindStopViolations(sheet, incoming, Culture));
    }

    [Fact]
    public void Warning_and_information_rules_do_not_block()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.AddValidation(new DataValidation(ValidationKind.WholeNumber, CellRange.Parse("A1:A1"))
        {
            Operator = ValidationOperator.Between,
            Operand1 = 1,
            Operand2 = 10,
            Response = ValidationResponse.Warning,
        });
        var incoming = new (int, int, CellValue, bool)[] { (0, 0, CellValue.Number(99), false) };

        Assert.Empty(ValidationPreflight.FindStopViolations(sheet, incoming, Culture));
    }

    [Fact]
    public void List_rule_backed_by_a_range_uses_the_resolver()
    {
        var sheet = new Worksheet("Sheet1");
        sheet.SetValue(0, 5, CellValue.Text("Red"));
        sheet.SetValue(1, 5, CellValue.Text("Green"));
        sheet.AddValidation(new DataValidation(ValidationKind.List, CellRange.Parse("A1:A2"))
        {
            ListSource = CellRange.Parse("F1:F2"),
        });
        IEnumerable<CellValue> Resolve(CellRange r) => r.Positions().Select(p => sheet.GetValue(p.Row, p.Column));

        var incoming = new (int, int, CellValue, bool)[]
        {
            (0, 0, CellValue.Text("Green"), false), // in list
            (1, 0, CellValue.Text("Blue"), false),  // not in list → violation
        };

        Assert.Equal(new[] { (1, 0) }, ValidationPreflight.FindStopViolations(sheet, incoming, Culture, Resolve));
    }
}
