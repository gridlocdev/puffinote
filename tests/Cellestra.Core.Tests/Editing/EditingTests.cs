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
    public void SetHyperlink_sets_and_undoes()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        var link = new Hyperlink("https://example.com") { Tooltip = "Example" };
        history.Execute(EditCommands.SetHyperlink(workbook, sheet.Name, 0, 0, link));
        Assert.Equal(link, sheet.Find(0, 0)!.Hyperlink);

        history.Undo();
        Assert.Null(sheet.Find(0, 0)?.Hyperlink);

        history.Redo();
        Assert.Equal("https://example.com", sheet.Find(0, 0)!.Hyperlink!.Target);
    }

    [Fact]
    public void SetHyperlink_null_clears_an_existing_link_and_undo_restores_it()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        // A cell with a value and a link, so clearing the link must keep the value.
        history.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Text("Anchor")));
        history.Execute(EditCommands.SetHyperlink(workbook, sheet.Name, 0, 0, new Hyperlink("https://a.test")));

        history.Execute(EditCommands.SetHyperlink(workbook, sheet.Name, 0, 0, null));
        Assert.Null(sheet.Find(0, 0)!.Hyperlink);
        Assert.Equal("Anchor", sheet.GetValue(0, 0).AsText);

        history.Undo(); // restores the cleared link
        Assert.Equal("https://a.test", sheet.Find(0, 0)!.Hyperlink!.Target);
    }

    [Fact]
    public void SetHyperlink_null_on_an_otherwise_blank_cell_prunes_it()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        history.Execute(EditCommands.SetHyperlink(workbook, sheet.Name, 0, 0, new Hyperlink("https://a.test")));
        history.Execute(EditCommands.SetHyperlink(workbook, sheet.Name, 0, 0, null));

        // No value, no formula, no style, no link → the cell should be pruned away entirely.
        Assert.Null(sheet.Find(0, 0));
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
    public void Rewriting_a_cells_text_preserves_its_alignment()
    {
        // Find & Replace rewrites a matched cell's text through SetInput. A cell the user explicitly
        // aligned (e.g. a centered header) must keep that alignment after the replacement.
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        history.Execute(EditCommands.SetInput(sheet.Name, 0, 0, "Total", CultureInfo.InvariantCulture));
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A1"),
            s => s with { Alignment = s.Alignment with { Horizontal = HorizontalAlignment.Center, Vertical = VerticalAlignment.Middle } }));

        history.Execute(EditCommands.SetInput(sheet.Name, 0, 0, "Sum", CultureInfo.InvariantCulture));

        var cell = sheet.Find(0, 0)!;
        Assert.Equal("Sum", cell.Value.AsText);
        var alignment = workbook.Styles.Get(cell.StyleId).Alignment;
        Assert.Equal(HorizontalAlignment.Center, alignment.Horizontal);
        Assert.Equal(VerticalAlignment.Middle, alignment.Vertical);
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
    public void Inserted_row_inherits_formatting_from_the_row_above()
    {
        var (workbook, history) = New();
        var sheet = workbook.ActiveSheet;

        // Bold A2 (row index 1), then insert a blank row directly below it (at index 2).
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A2"),
            s => s.WithFont(s.Font.WithBold(true))));
        int styledId = sheet.Find(1, 0)!.StyleId;
        Assert.NotEqual(0, styledId);

        history.Execute(new StructuralEditCommand(sheet.Name, StructuralAxis.Row, StructuralOperation.Insert, at: 2, count: 1));

        var inherited = sheet.Find(2, 0);
        Assert.NotNull(inherited);
        Assert.Equal(styledId, inherited!.StyleId); // formatting carried down
        Assert.True(inherited.Value.IsEmpty);       // but no value

        history.Undo();
        Assert.Null(sheet.Find(2, 0)); // the inherited styled cell is removed on undo
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
