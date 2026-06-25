using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>The ⌥⇧-arrow move and the press-and-hold radial menu, driven through the live editor.</summary>
public class RadialMenuTests
{
    [AvaloniaFact]
    public void Option_shift_arrow_moves_contents_and_makes_references_follow()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(10));            // A1
        sheet.SetFormula(0, 2, "A1*2", CellValue.Number(20));  // C1 = A1*2 (points at A1)
        window.Editor.Session.RebuildDependencies();           // formulas set outside an edit — refresh the graph
        window.Editor.Selection.MoveTo(0, 0);                  // select A1

        window.Editor.Grid.MoveSelectionForTest(1, 0);         // ⌥⇧Down: nudge A1 → A2

        Assert.Null(sheet.Find(0, 0));                         // A1 cleared
        Assert.Equal(10d, sheet.Find(1, 0)!.Value.AsNumber);  // its value moved to A2
        Assert.Equal("A2*2", sheet.Find(0, 2)!.Formula);      // the reference followed the cell
        Assert.Equal(1, window.Editor.Selection.ActiveRow);   // selection followed the move

        window.Editor.Session.Undo();
        Assert.Equal(10d, sheet.Find(0, 0)!.Value.AsNumber);  // undo restores the original layout
        Assert.Equal("A1*2", sheet.Find(0, 2)!.Formula);
    }

    [AvaloniaFact]
    public void Option_shift_arrow_swaps_with_an_occupied_neighbour()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(10)); // A1
        sheet.SetValue(0, 1, CellValue.Number(20)); // B1
        window.Editor.Selection.MoveTo(0, 0);       // select A1

        window.Editor.Grid.MoveSelectionForTest(0, 1); // ⌥⇧Right onto the occupied B1

        // Non-destructive: the two cells exchange rather than B1 being overwritten.
        Assert.Equal(20d, sheet.Find(0, 0)!.Value.AsNumber);
        Assert.Equal(10d, sheet.Find(0, 1)!.Value.AsNumber);
        Assert.Equal(1, window.Editor.Selection.ActiveColumn); // selection followed to B1
    }

    [AvaloniaFact]
    public void Swap_through_the_session_follows_references_via_the_dependency_index()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(10));         // A1
        sheet.SetValue(0, 1, CellValue.Number(20));         // B1
        sheet.SetFormula(0, 2, "A1", CellValue.Number(10)); // C1 = A1
        sheet.SetFormula(0, 3, "B1", CellValue.Number(20)); // D1 = B1
        window.Editor.Session.RebuildDependencies();        // make the graph current for the index path
        window.Editor.Selection.MoveTo(0, 0);

        window.Editor.Grid.MoveSelectionForTest(0, 1); // ⌥⇧Right: swap A1 ⇄ B1

        // Both references follow their data to its new cell — exercised through the engine's reverse
        // index (SpreadsheetEditorView.AffectedFormulas → WorkbookSession.FormulasReferencing), not a scan.
        Assert.Equal("B1", sheet.Find(0, 2)!.Formula);
        Assert.Equal("A1", sheet.Find(0, 3)!.Formula);
    }

    [AvaloniaFact]
    public void Move_is_refused_off_the_top_edge()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(7)); // A1
        window.Editor.Selection.MoveTo(0, 0);

        window.Editor.Grid.MoveSelectionForTest(-1, 0); // ⌥⇧Up from row 1 — nowhere to go

        Assert.Equal(7d, sheet.Find(0, 0)!.Value.AsNumber); // unchanged; the cell stayed put
    }

    [AvaloniaFact]
    public void Radial_segments_resolve_commands_from_the_config()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.RadialConfig = new RadialMenuConfig
        {
            SegmentCount = 3,
            ShowLabels = true,
            CommandIds = new List<string?> { "edit.copy", null, "edit.paste" },
        };

        var segments = window.Editor.Grid.RadialSegmentsForTest();

        Assert.Equal(3, segments.Count);
        Assert.Equal("edit.copy", segments[0].CommandId);
        Assert.Equal("Copy", segments[0].Label);
        Assert.Null(segments[1].CommandId);   // the unbound wedge
        Assert.False(segments[1].Enabled);
        Assert.Equal("edit.paste", segments[2].CommandId);
    }

    [AvaloniaFact]
    public void Releasing_on_a_wedge_runs_its_bound_command()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1)); // a non-empty cell so Bold has something to toggle
        window.Editor.Selection.MoveTo(0, 0);

        var bold = AppCommands.ById("format.bold")!;
        Assert.False(bold.IsToggleActive(window.Editor));

        window.Editor.Grid.ActivateRadialForTest("format.bold");

        Assert.True(bold.IsToggleActive(window.Editor)); // the radial ran the command against the selection
    }

    [AvaloniaFact]
    public void Settings_dialog_builds_the_edited_config()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            ShowLabels = false,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        var result = dialog.BuildConfigForTest();

        Assert.Equal(4, result.SegmentCount);
        Assert.False(result.ShowLabels);
        Assert.Equal("edit.cut", result.CommandIdAt(0));
        Assert.Equal("edit.undo", result.CommandIdAt(3));
    }
}
