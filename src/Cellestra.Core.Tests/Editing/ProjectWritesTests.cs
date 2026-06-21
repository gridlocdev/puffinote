using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

/// <summary>
/// The validation pre-flight projections must match what the commands actually write, or paste/fill
/// could be blocked (or allowed) for the wrong cells.
/// </summary>
public class ProjectWritesTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    [Fact]
    public void Paste_projection_matches_applied_cells()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(10));            // A1
        sheet.SetFormula(0, 1, "A1*2", CellValue.Number(20));  // B1 = A1*2

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("A1:B1"), isCut: false);
        var command = new PasteBlockCommand(sheet.Name, 2, 0, block); // anchor A3

        var projected = command.ProjectWrites().ToList();
        history.Execute(command);

        Assert.NotEmpty(projected);
        foreach (var (row, column, value, isFormula) in projected)
        {
            var cell = sheet.Find(row, column);
            Assert.NotNull(cell);
            Assert.Equal(value, cell!.Value);
            Assert.Equal(isFormula, cell.Formula is not null);
        }
    }

    [Fact]
    public void Fill_projection_matches_applied_cells()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(7)); // A1

        var command = new FillCommand(sheet.Name, CellRange.Parse("A1"), CellRange.Parse("A1:A3"));

        var projected = command.ProjectWrites(workbook).ToList();
        history.Execute(command);

        Assert.Equal(2, projected.Count); // A2, A3 (source A1 stays put)
        foreach (var (row, column, value, isFormula) in projected)
        {
            var cell = sheet.Find(row, column);
            Assert.NotNull(cell);
            Assert.Equal(value, cell!.Value);
            Assert.Equal(isFormula, cell.Formula is not null);
        }
    }
}
