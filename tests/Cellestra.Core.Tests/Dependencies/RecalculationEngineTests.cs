using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Dependencies;

public class RecalculationEngineTests
{
    private static (Workbook Workbook, Worksheet Sheet) NewBook()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, workbook.ActiveSheet);
    }

    [Fact]
    public void RecalculateAll_computes_formula_cells()
    {
        var (workbook, sheet) = NewBook();
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetValue(1, 0, CellValue.Number(2));
        sheet.SetFormula(2, 0, "A1+A2", CellValue.Empty);

        var engine = new RecalculationEngine(workbook);
        engine.RebuildGraph();
        engine.RecalculateAll();

        Assert.Equal(3, sheet.GetValue(2, 0).AsNumber);
    }

    [Fact]
    public void RecalculateAll_orders_a_chain()
    {
        var (workbook, sheet) = NewBook();
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetFormula(0, 1, "A1+1", CellValue.Empty);   // B1 = A1 + 1
        sheet.SetFormula(0, 2, "B1+1", CellValue.Empty);   // C1 = B1 + 1

        var engine = new RecalculationEngine(workbook);
        engine.RebuildGraph();
        engine.RecalculateAll();

        Assert.Equal(2, sheet.GetValue(0, 1).AsNumber);
        Assert.Equal(3, sheet.GetValue(0, 2).AsNumber);
    }

    [Fact]
    public void Edit_propagates_to_dependents()
    {
        var (workbook, sheet) = NewBook();
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetFormula(0, 1, "A1+1", CellValue.Empty);
        sheet.SetFormula(0, 2, "B1+1", CellValue.Empty);

        var engine = new RecalculationEngine(workbook);
        engine.RebuildGraph();
        engine.RecalculateAll();

        sheet.SetValue(0, 0, CellValue.Number(10));
        var result = engine.RecordEdits(new[] { new CellRef(sheet.Name, 0, 0) });

        Assert.Equal(11, sheet.GetValue(0, 1).AsNumber);
        Assert.Equal(12, sheet.GetValue(0, 2).AsNumber);
        Assert.Equal(2, result.Evaluated);
    }

    [Fact]
    public void Circular_reference_is_detected_and_reported()
    {
        var (workbook, sheet) = NewBook();
        sheet.SetFormula(0, 0, "B1", CellValue.Empty);
        sheet.SetFormula(0, 1, "A1", CellValue.Empty);

        var engine = new RecalculationEngine(workbook);
        engine.RebuildGraph();
        var result = engine.RecalculateAll();

        Assert.True(result.HasCircularReference);
        Assert.Equal("#REF!", sheet.GetValue(0, 0).AsError.Token);
        Assert.Equal("#REF!", sheet.GetValue(0, 1).AsError.Token);
    }

    [Fact]
    public void Manual_mode_defers_recalculation()
    {
        var (workbook, sheet) = NewBook();
        workbook.Settings.CalculationMode = CalculationMode.Manual;
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetFormula(0, 1, "A1+1", CellValue.Number(99));

        var engine = new RecalculationEngine(workbook);
        engine.RebuildGraph();

        var result = engine.RecordEdits(new[] { new CellRef(sheet.Name, 0, 0) });

        Assert.Equal(0, result.Evaluated);
        Assert.Equal(99, sheet.GetValue(0, 1).AsNumber); // still the cached value
    }

    [Fact]
    public void Cross_sheet_reference_recalculates()
    {
        var workbook = Workbook.CreateNew();
        var sheet1 = workbook.ActiveSheet;
        var sheet2 = workbook.AddSheet();
        sheet2.SetValue(0, 0, CellValue.Number(42));
        sheet1.SetFormula(0, 0, $"{sheet2.Name}.A1*2", CellValue.Empty);

        var engine = new RecalculationEngine(workbook);
        engine.RebuildGraph();
        engine.RecalculateAll();

        Assert.Equal(84, sheet1.GetValue(0, 0).AsNumber);
    }

    [Fact]
    public void Volatile_function_is_tracked()
    {
        var (workbook, sheet) = NewBook();
        sheet.SetFormula(0, 0, "TODAY()", CellValue.Empty);

        var engine = new RecalculationEngine(workbook);
        engine.RebuildGraph();

        Assert.Contains(new CellRef(sheet.Name, 0, 0), engine.VolatileCells);
    }
}
