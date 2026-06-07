using System.IO;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;
using Cellestra.Desktop.Services;
using Xunit;

namespace Cellestra.UiTests;

public class RecalcMismatchTests
{
    private static Stream WriteOds(Workbook workbook)
    {
        var memory = new MemoryStream();
        OdsWriter.Write(workbook, memory, null);
        memory.Position = 0;
        return memory;
    }

    [Fact]
    public void Open_flags_cells_that_recompute_differently_than_the_cache()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetFormula(1, 0, "A1+1", CellValue.Number(999)); // stale cache; the real result is 2

        using var stream = WriteOds(workbook);
        var session = WorkbookSession.NewBlank();
        session.LoadFrom(stream, "tampered.ods");

        Assert.Equal(1, session.LoadRecalcMismatches);
        Assert.Equal(2, session.ActiveSheet.GetValue(1, 0).AsNumber); // recomputed value is shown
    }

    [Fact]
    public void Open_reports_no_mismatch_when_the_cache_is_correct()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1));
        sheet.SetFormula(1, 0, "A1+1", CellValue.Number(2)); // correct cache

        using var stream = WriteOds(workbook);
        var session = WorkbookSession.NewBlank();
        session.LoadFrom(stream, "fresh.ods");

        Assert.Equal(0, session.LoadRecalcMismatches);
    }
}
