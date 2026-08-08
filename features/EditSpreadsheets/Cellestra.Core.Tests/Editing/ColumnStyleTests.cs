using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

/// <summary>
/// Formatting a whole column is one default style on the column, not a style stamped onto each of its
/// 1,048,576 cells — ODF's <c>table:default-cell-style-name</c>. The old behaviour sat exactly on the
/// in-place cell cap, so it took the verbatim path and materialized the entire column: slow to apply,
/// slow to save, and (the damaging part) it left the sheet's used range reading A1:A1048576, which is
/// what bounds Ctrl+End, auto-fit, CSV export, and the clamp that protects every later whole-sheet edit.
/// </summary>
public class ColumnStyleTests
{
    private static (Workbook Workbook, EditHistory History, Worksheet Sheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    private static CellRange WholeColumn(int column) =>
        new(new CellAddress(column, 0), new CellAddress(column, SheetDimensions.MaxRows - 1));

    private static IEditCommand Wrap(Workbook workbook, Worksheet sheet, CellRange range) =>
        EditCommands.ApplyStyle(workbook, sheet.Name, range,
            s => s.WithAlignment(s.Alignment with { WrapText = true }), "Wrap Text");

    [Fact]
    public void Wrapping_a_whole_column_materializes_no_cells()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("hello"));

        history.Execute(Wrap(workbook, sheet, WholeColumn(0)));

        Assert.Single(sheet.EnumerateCells());
        Assert.Equal(CellRange.Parse("A1"), sheet.GetUsedRange());
        Assert.True(workbook.Styles.Get(sheet.FindColumn(0)!.StyleId).Alignment.WrapText);
    }

    [Fact]
    public void A_blank_cell_in_the_column_reads_as_wrapped()
    {
        // The whole point of the column default: a cell nobody has typed in yet still looks — and
        // behaves — formatted, which is what the inline editor asks about when you open it.
        var (workbook, history, sheet) = New();
        history.Execute(Wrap(workbook, sheet, WholeColumn(0)));

        Assert.True(workbook.Styles.Get(sheet.EffectiveStyleId(500, 0)).Alignment.WrapText);
        Assert.False(workbook.Styles.Get(sheet.EffectiveStyleId(500, 1)).Alignment.WrapText);
        Assert.Null(sheet.Find(500, 0)); // ...and it is still blank
    }

    [Fact]
    public void A_cell_with_a_style_of_its_own_takes_the_transform_directly()
    {
        // A styled cell isn't covered by the column default, so it has to be transformed in place or
        // the column would appear to skip it.
        var (workbook, history, sheet) = New();
        sheet.SetValue(3, 0, CellValue.Text("bold already"));
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A4"),
            s => s.WithFont(s.Font.WithBold(true))));

        history.Execute(Wrap(workbook, sheet, WholeColumn(0)));

        var style = workbook.Styles.Get(sheet.EffectiveStyleId(3, 0));
        Assert.True(style.Alignment.WrapText);
        Assert.True(style.Font.Bold); // its own formatting survived
    }

    [Fact]
    public void Undo_restores_the_column_and_the_cells_it_touched()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(3, 0, CellValue.Text("x"));
        history.Execute(EditCommands.ApplyStyle(workbook, sheet.Name, CellRange.Parse("A4"),
            s => s.WithFont(s.Font.WithBold(true))));

        history.Execute(Wrap(workbook, sheet, WholeColumn(0)));
        history.Undo();

        Assert.False(workbook.Styles.Get(sheet.EffectiveStyleId(500, 0)).Alignment.WrapText);
        var style = workbook.Styles.Get(sheet.EffectiveStyleId(3, 0));
        Assert.False(style.Alignment.WrapText);
        Assert.True(style.Font.Bold); // the earlier edit is untouched
    }

    [Fact]
    public void A_cells_own_style_wins_over_its_column_and_row()
    {
        var (workbook, _, sheet) = New();
        var wrapped = workbook.Styles.Intern(CellStyle.Default.WithAlignment(
            CellStyle.Default.Alignment with { WrapText = true }));
        var bold = workbook.Styles.Intern(CellStyle.Default.WithFont(CellStyle.Default.Font.WithBold(true)));
        var italic = workbook.Styles.Intern(CellStyle.Default.WithFont(CellStyle.Default.Font.WithItalic(true)));

        sheet.GetOrCreateColumn(0).StyleId = wrapped;
        Assert.Equal(wrapped, sheet.EffectiveStyleId(0, 0));

        sheet.GetOrCreateRow(0).StyleId = bold;
        Assert.Equal(bold, sheet.EffectiveStyleId(0, 0)); // the row is the nearer scope

        sheet.SetValue(0, 0, CellValue.Text("v"));
        sheet.Find(0, 0)!.StyleId = italic;
        Assert.Equal(italic, sheet.EffectiveStyleId(0, 0)); // the cell's own wins outright
    }

    [Fact]
    public void A_columns_default_style_round_trips_through_ods()
    {
        var (workbook, history, sheet) = New();
        sheet.SetValue(0, 0, CellValue.Text("hello"));
        history.Execute(Wrap(workbook, sheet, WholeColumn(0)));

        byte[] bytes = OdsWriter.WriteToBytes(workbook);
        using var stream = new MemoryStream(bytes);
        var reopened = OdsReader.Read(stream);
        var reopenedSheet = reopened.ActiveSheet;

        Assert.True(reopened.Styles.Get(reopenedSheet.EffectiveStyleId(500, 0)).Alignment.WrapText);
        Assert.False(reopened.Styles.Get(reopenedSheet.EffectiveStyleId(500, 1)).Alignment.WrapText);
        Assert.Single(reopenedSheet.EnumerateCells()); // still not a million cells
    }

    [Fact]
    public void A_formatted_but_empty_column_survives_a_save()
    {
        // Nothing in the column and nothing to its left: the used range doesn't reach it, so the
        // writer has to be told to emit it anyway or the formatting silently disappears on save.
        var (workbook, history, sheet) = New();
        history.Execute(Wrap(workbook, sheet, WholeColumn(4)));

        byte[] bytes = OdsWriter.WriteToBytes(workbook);
        using var stream = new MemoryStream(bytes);
        var reopened = OdsReader.Read(stream);

        Assert.True(reopened.Styles.Get(reopened.ActiveSheet.EffectiveStyleId(0, 4)).Alignment.WrapText);
    }
}
