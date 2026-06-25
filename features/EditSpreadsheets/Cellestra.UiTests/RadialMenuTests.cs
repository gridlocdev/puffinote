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
