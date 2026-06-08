using Cellestra.Core.Dependencies;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class ClearCommandTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    [Fact]
    public void Clear_formatting_resets_style_but_keeps_content()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Number(42));
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true))));
        Assert.NotEqual(StylePool.DefaultId, sheet.Find(0, 0)!.StyleId);

        history.Execute(EditCommands.ClearFormatting(workbook, sheet.Name, CellRange.Parse("A1")));

        Assert.Equal(StylePool.DefaultId, sheet.Find(0, 0)!.StyleId); // formatting gone
        Assert.Equal(42, sheet.GetValue(0, 0).AsNumber);              // content stays

        history.Undo();
        Assert.True(workbook.Styles.Get(sheet.Find(0, 0)!.StyleId).Font.Bold); // restored
    }

    [Fact]
    public void Clear_all_drops_content_formula_style_and_hyperlink()
    {
        var (workbook, history, sheet) = New();
        sheet.SetFormula(0, 0, "1+1", CellValue.Number(2));
        var cell = sheet.Find(0, 0)!;
        cell.Hyperlink = new Hyperlink("https://example.com");
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true))));

        history.Execute(EditCommands.ClearAll(sheet.Name, CellRange.Parse("A1")));

        Assert.Null(sheet.Find(0, 0)); // cell pruned entirely

        history.Undo();
        var restored = sheet.Find(0, 0)!;
        Assert.Equal("1+1", restored.Formula);
        Assert.NotNull(restored.Hyperlink);
        Assert.True(workbook.Styles.Get(restored.StyleId).Font.Bold);
    }
}
