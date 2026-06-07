using System.Globalization;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class EditingTests
{
    private static (Workbook, EditHistory) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook));
    }

    [Fact]
    public void Set_value_is_undoable()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        history.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Number(5)));
        Assert.Equal(5, sheet.GetValue(0, 0).AsNumber);

        history.Undo();
        Assert.True(sheet.GetValue(0, 0).IsEmpty);

        history.Redo();
        Assert.Equal(5, sheet.GetValue(0, 0).AsNumber);
    }

    [Fact]
    public void SetInput_chooses_formula_or_value()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        history.Execute(EditCommands.SetInput(sheet.Name, 0, 0, "=A2+1", CultureInfo.InvariantCulture));
        history.Execute(EditCommands.SetInput(sheet.Name, 1, 0, "42", CultureInfo.InvariantCulture));

        Assert.Equal("A2+1", sheet.Find(0, 0)!.Formula);
        Assert.Equal(42, sheet.GetValue(1, 0).AsNumber);
    }

    [Fact]
    public void SetInput_stamps_a_date_format_on_a_typed_date()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;
        var enUs = CultureInfo.GetCultureInfo("en-US");

        history.Execute(EditCommands.SetInput(workbook, sheet.Name, 0, 0, "6/7/2026", enUs));

        Assert.True(sheet.GetValue(0, 0).IsNumber); // stored as a serial
        Assert.Equal(NumberFormatKind.Date, workbook.Styles.Get(sheet.Find(0, 0)!.StyleId).NumberFormat.Kind);

        history.Undo();
        Assert.True(sheet.GetValue(0, 0).IsEmpty); // value and the stamped style both revert
    }

    [Fact]
    public void Deleting_a_referenced_sheet_breaks_dependents_and_undoes()
    {
        var (workbook, history) = New();
        var sheet1 = workbook.ActiveSheet;
        workbook.AddSheet("Data");
        history.Execute(EditCommands.SetFormula(sheet1.Name, 0, 0, "Data.A1+1"));

        history.Execute(new DeleteSheetCommand("Data"));

        Assert.Null(workbook.FindSheet("Data"));
        Assert.Equal("#REF!+1", sheet1.Find(0, 0)!.Formula);

        history.Undo();

        Assert.NotNull(workbook.FindSheet("Data"));
        Assert.Equal("Data.A1+1", sheet1.Find(0, 0)!.Formula);
    }

    [Fact]
    public void Apply_style_toggles_bold_and_undoes()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("x"));

        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A1"),
            s => s.WithFont(s.Font.WithBold(true)), "Bold"));
        Assert.True(workbook.Styles.Get(sheet.Find(0, 0)!.StyleId).Font.Bold);

        history.Undo();
        Assert.False(workbook.Styles.Get(sheet.Find(0, 0)!.StyleId).Font.Bold);
    }

    [Fact]
    public void History_respects_cap()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;
        for (int i = 0; i < EditHistory.MaxDepth + 50; i++)
        {
            history.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Number(i)));
        }

        int undone = 0;
        while (history.CanUndo)
        {
            history.Undo();
            undone++;
        }

        Assert.Equal(EditHistory.MaxDepth, undone);
    }

    [Fact]
    public void Insert_rows_shifts_cells_and_adjusts_formulas()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1));   // A1
        sheet.SetValue(4, 0, CellValue.Number(5));   // A5
        sheet.SetFormula(5, 0, "A5*2", CellValue.Empty); // A6 references A5

        history.Execute(new StructuralEditCommand(sheet.Name, StructuralAxis.Row, StructuralOperation.Insert, 2, 1));

        Assert.Equal(5, sheet.GetValue(5, 0).AsNumber);     // A5 -> A6
        Assert.Equal("A6*2", sheet.Find(6, 0)!.Formula);    // formula adjusted

        history.Undo();
        Assert.Equal(5, sheet.GetValue(4, 0).AsNumber);     // back to A5
        Assert.Equal("A5*2", sheet.Find(5, 0)!.Formula);
    }

    [Fact]
    public void Delete_rows_breaks_reference_to_deleted_cell()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(2, 0, CellValue.Number(9));        // A3
        sheet.SetFormula(0, 1, "A3+1", CellValue.Empty);  // B1 references A3

        history.Execute(new StructuralEditCommand(sheet.Name, StructuralAxis.Row, StructuralOperation.Delete, 2, 1));

        Assert.Equal("#REF!+1", sheet.Find(0, 1)!.Formula);
    }

    [Fact]
    public void Merge_and_unmerge_are_undoable()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        history.Execute(EditCommands.MergeCells(sheet.Name, CellRange.Parse("A1:B2")));
        Assert.NotNull(sheet.FindMerge(0, 0));

        history.Undo();
        Assert.Null(sheet.FindMerge(0, 0));
    }

    [Fact]
    public void Hide_columns_is_undoable()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        history.Execute(EditCommands.SetColumnsHidden(sheet.Name, 1, 2, true));
        Assert.True(sheet.IsColumnHidden(1));
        Assert.True(sheet.IsColumnHidden(2));

        history.Undo();
        Assert.False(sheet.IsColumnHidden(1));
    }
}
