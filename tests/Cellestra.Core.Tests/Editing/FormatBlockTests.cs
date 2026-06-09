using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Editing;

public class FormatBlockTests
{
    private static (Workbook, EditHistory, Worksheet) New()
    {
        var workbook = Workbook.CreateNew();
        return (workbook, new EditHistory(workbook), workbook.ActiveSheet);
    }

    private static int Style(Worksheet sheet, int row, int column) =>
        sheet.GetOrCreate(row, column).StyleId;

    [Fact]
    public void Painting_copies_only_formatting_not_values()
    {
        var (workbook, history, sheet) = New();
        // Source A1: bold + yellow fill, with a value.
        int sourceStyle = workbook.Styles.Intern(CellStyle.Default
            .WithFont(Font.Default.WithBold(true))
            .WithFill(Fill.Solid(ArgbColor.FromHex("#FFFF00"))));
        sheet.SetValue(0, 0, CellValue.Text("source"));
        sheet.GetOrCreate(0, 0).StyleId = sourceStyle;

        // Target B2 has a value of its own.
        sheet.SetValue(1, 1, CellValue.Number(42));

        var stamp = FormatBlock.Capture(workbook, sheet, CellRange.Parse("A1"));
        history.Execute(stamp.ApplyTo(sheet.Name, CellRange.Parse("B2")));

        // Style copied …
        var painted = workbook.Styles.Get(Style(sheet, 1, 1));
        Assert.True(painted.Font.Bold);
        Assert.Equal(ArgbColor.FromHex("#FFFF00"), painted.Fill.Background);
        // … value untouched.
        Assert.Equal(42, sheet.GetValue(1, 1).AsNumber);
        // Source value untouched.
        Assert.Equal("source", sheet.GetValue(0, 0).AsText);
    }

    [Fact]
    public void Painting_tiles_a_multi_cell_pattern_over_the_target()
    {
        var (workbook, history, sheet) = New();
        int boldStyle = workbook.Styles.Intern(CellStyle.Default.WithFont(Font.Default.WithBold(true)));
        sheet.GetOrCreate(0, 0).StyleId = boldStyle; // A1 bold, B1 default

        var stamp = FormatBlock.Capture(workbook, sheet, CellRange.Parse("A1:B1"));
        history.Execute(stamp.ApplyTo(sheet.Name, CellRange.Parse("A3:D3")));

        Assert.True(workbook.Styles.Get(Style(sheet, 2, 0)).Font.Bold);   // A3 ← A1
        Assert.False(workbook.Styles.Get(Style(sheet, 2, 1)).Font.Bold);  // B3 ← B1
        Assert.True(workbook.Styles.Get(Style(sheet, 2, 2)).Font.Bold);   // C3 ← A1 (tiled)
        Assert.False(workbook.Styles.Get(Style(sheet, 2, 3)).Font.Bold);  // D3 ← B1 (tiled)
    }

    [Fact]
    public void Painting_is_undoable_and_restores_styles()
    {
        var (workbook, history, sheet) = New();
        int boldStyle = workbook.Styles.Intern(CellStyle.Default.WithFont(Font.Default.WithBold(true)));
        sheet.GetOrCreate(0, 0).StyleId = boldStyle;

        var stamp = FormatBlock.Capture(workbook, sheet, CellRange.Parse("A1"));
        history.Execute(stamp.ApplyTo(sheet.Name, CellRange.Parse("B2")));
        Assert.True(workbook.Styles.Get(Style(sheet, 1, 1)).Font.Bold);

        history.Undo();
        Assert.Equal(StylePool.DefaultId, sheet.GetOrCreate(1, 1).StyleId);
    }

    [Fact]
    public void Painting_replicates_merge_from_the_source()
    {
        var (workbook, history, sheet) = New();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("A1:B1")));

        var stamp = FormatBlock.Capture(workbook, sheet, CellRange.Parse("A1:B1"));
        history.Execute(stamp.ApplyTo(sheet.Name, CellRange.Parse("A3:B3")));

        var merge = sheet.FindMerge(2, 0);
        Assert.NotNull(merge);
        Assert.Equal(CellRange.Parse("A3:B3"), merge!.Range);

        // Undo removes the painted merge again.
        history.Undo();
        Assert.Null(sheet.FindMerge(2, 0));
    }

    [Fact]
    public void Painting_clears_an_existing_target_merge_when_source_has_one()
    {
        var (workbook, history, sheet) = New();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("A1:B1"))); // source merge
        sheet.AddMerge(new MergedRegion(CellRange.Parse("A3:A4"))); // pre-existing target merge

        var stamp = FormatBlock.Capture(workbook, sheet, CellRange.Parse("A1:B1"));
        history.Execute(stamp.ApplyTo(sheet.Name, CellRange.Parse("A3:B3")));

        // Old target merge gone; new horizontal merge in its place.
        var merge = sheet.FindMerge(2, 0);
        Assert.NotNull(merge);
        Assert.Equal(CellRange.Parse("A3:B3"), merge!.Range);

        history.Undo();
        // Original target merge restored exactly.
        var restored = sheet.FindMerge(2, 0);
        Assert.NotNull(restored);
        Assert.Equal(CellRange.Parse("A3:A4"), restored!.Range);
    }
}
