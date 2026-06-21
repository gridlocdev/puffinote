using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class CompositeEditCommandTests
{
    [Fact]
    public void Applies_all_parts_and_undoes_them_as_one_step()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;

        var composite = EditCommands.Composite("Two edits", new[]
        {
            EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Number(1)),
            EditCommands.SetValue(sheet.Name, 0, 1, CellValue.Number(2)),
        });

        composite.Apply(workbook);
        Assert.Equal(1, sheet.GetValue(0, 0).AsNumber);
        Assert.Equal(2, sheet.GetValue(0, 1).AsNumber);

        composite.Undo(workbook);
        Assert.True(sheet.GetValue(0, 0).IsEmpty);
        Assert.True(sheet.GetValue(0, 1).IsEmpty);
    }
}
