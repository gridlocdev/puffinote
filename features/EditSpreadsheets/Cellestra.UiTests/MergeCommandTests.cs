using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Merge Cells as a toggle (PRD §10.1). Clicking a merged cell selects the whole merge, so running the
/// command there used to ask the model to overlap a region that already existed — an exception nothing
/// caught, i.e. a crash on the most ordinary way to reach the command.
/// </summary>
public class MergeCommandTests
{
    private static (MainWindow Window, IAppEnvironment Env) Open()
    {
        var window = new MainWindow();
        window.Show();
        return (window, window.Editor);
    }

    [AvaloniaFact]
    public void Merging_a_selection_creates_the_merge()
    {
        var (_, env) = Open();
        var sheet = env.Session.ActiveSheet;
        env.Selection.SetRange(1, 1, 2, 2); // B2:C3

        env.Commands.ById("format.merge")!.Execute();

        Assert.Equal(CellRange.Parse("B2:C3"), sheet.FindMerge(1, 1)!.Range);
    }

    [AvaloniaFact]
    public void Merging_again_over_the_whole_merge_unmerges_it()
    {
        var (_, env) = Open();
        var sheet = env.Session.ActiveSheet;
        var merge = env.Commands.ById("format.merge")!;

        env.Selection.SetRange(1, 1, 2, 2);
        merge.Execute();
        Assert.NotNull(sheet.FindMerge(1, 1));

        // Clicking a merged cell selects the whole merge — this selection *is* what that looks like.
        env.Selection.SetRange(1, 1, 2, 2);
        merge.Execute(); // used to throw "Merged regions may not overlap"

        Assert.Null(sheet.FindMerge(1, 1));
        Assert.Empty(sheet.MergedRegions);
    }

    [AvaloniaFact]
    public void A_single_cell_inside_a_merge_unmerges_it()
    {
        var (_, env) = Open();
        var sheet = env.Session.ActiveSheet;
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3")));

        env.Selection.MoveTo(2, 2); // C3 — a covered cell, not the anchor
        env.Commands.ById("format.merge")!.Execute();

        Assert.Empty(sheet.MergedRegions);
    }

    [AvaloniaFact]
    public void A_bigger_selection_absorbs_the_merges_it_spans()
    {
        var (_, env) = Open();
        var sheet = env.Session.ActiveSheet;
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3")));
        sheet.AddMerge(new MergedRegion(CellRange.Parse("D2:D3")));

        env.Selection.SetRange(1, 1, 3, 3); // B2:D4 — spans both merges and then some
        env.Commands.ById("format.merge")!.Execute();

        var single = Assert.Single(sheet.MergedRegions);
        Assert.Equal(CellRange.Parse("B2:D4"), single.Range);
    }

    [AvaloniaFact]
    public void Absorbing_merges_undoes_in_one_step()
    {
        var (_, env) = Open();
        var sheet = env.Session.ActiveSheet;
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3")));

        env.Selection.SetRange(1, 1, 3, 3);
        env.Commands.ById("format.merge")!.Execute();
        env.Session.Undo();

        var single = Assert.Single(sheet.MergedRegions);
        Assert.Equal(CellRange.Parse("B2:C3"), single.Range);
    }

    [AvaloniaFact]
    public void A_lone_unmerged_cell_has_nothing_to_merge()
    {
        var (_, env) = Open();
        var sheet = env.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("alone"));

        env.Selection.MoveTo(0, 0);
        env.Commands.ById("format.merge")!.Execute();

        Assert.Empty(sheet.MergedRegions);
    }
}
