using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.RadialMenu;
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
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        var result = dialog.BuildConfigForTest();

        Assert.Equal(4, result.SegmentCount);
        Assert.Equal("edit.cut", result.CommandIdAt(0));
        Assert.Equal("edit.undo", result.CommandIdAt(3));
    }

    [AvaloniaFact]
    public void Settings_dialog_bolds_the_hovered_segment_row()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        dialog.HoverSegmentForTest(2);
        Assert.True(dialog.IsRowBoldForTest(2));
        Assert.False(dialog.IsRowBoldForTest(0));

        dialog.HoverSegmentForTest(-1); // pointer left the preview
        Assert.False(dialog.IsRowBoldForTest(2));
    }

    [AvaloniaFact]
    public void Settings_dialog_reset_restores_defaults_only_after_confirming()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        dialog.OpenResetConfirmForTest();
        Assert.True(dialog.ResetConfirmVisibleForTest);

        dialog.ConfirmResetForTest();
        Assert.False(dialog.ResetConfirmVisibleForTest);

        var result = dialog.BuildConfigForTest();
        var defaults = RadialMenuConfig.Default;
        Assert.Equal(defaults.SegmentCount, result.SegmentCount);
        Assert.Equal(defaults.CommandIdAt(0), result.CommandIdAt(0));
    }

    [AvaloniaFact]
    public void Settings_dialog_swap_reorders_two_segments()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        dialog.DragReorderForTest(0, 2); // drag segment 1 onto segment 3 — they swap

        var result = dialog.BuildConfigForTest();
        Assert.Equal("edit.paste", result.CommandIdAt(0));
        Assert.Equal("edit.cut", result.CommandIdAt(2));
        Assert.Equal("edit.copy", result.CommandIdAt(1)); // untouched
        Assert.Equal(2, dialog.SelectedSegmentForTest);   // selection follows the dragged item
    }

    [AvaloniaFact]
    public void Settings_dialog_alt_shift_arrow_moves_the_selected_segment()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        dialog.SelectSegmentForTest(1);
        dialog.MoveSelectedForTest(+1); // ⌥⇧↓ : segment 2 (copy) trades places with segment 3 (paste)

        var result = dialog.BuildConfigForTest();
        Assert.Equal("edit.paste", result.CommandIdAt(1));
        Assert.Equal("edit.copy", result.CommandIdAt(2));
        Assert.Equal(2, dialog.SelectedSegmentForTest);
        Assert.True(dialog.IsRowSelectedForTest(2));
        Assert.Equal(2, dialog.PreviewActiveSegmentForTest); // the moved wedge lights up in the preview

        dialog.MoveSelectedForTest(+10); // off the bottom edge — refused, no change
        Assert.Equal("edit.copy", dialog.BuildConfigForTest().CommandIdAt(2));
    }

    [AvaloniaFact]
    public void Settings_dialog_keyboard_selection_highlights_the_preview_wedge()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        Assert.Equal(-1, dialog.PreviewActiveSegmentForTest); // nothing selected, nothing hovered

        dialog.SelectSegmentForTest(3);
        Assert.Equal(3, dialog.PreviewActiveSegmentForTest);

        // A transient hover wins while it lasts, then the selection highlight returns.
        dialog.HoverSegmentForTest(1);
        Assert.Equal(1, dialog.PreviewActiveSegmentForTest);
        dialog.HoverSegmentForTest(-1);
        Assert.Equal(3, dialog.PreviewActiveSegmentForTest);
    }

    [AvaloniaFact]
    public void Escape_with_unsaved_changes_prompts_to_save_or_cancel()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        Assert.False(dialog.IsDirtyForTest); // nothing touched yet

        dialog.DragReorderForTest(0, 1); // a real edit
        Assert.True(dialog.IsDirtyForTest);

        Assert.True(dialog.PressEscapeForTest());        // Escape is intercepted...
        Assert.True(dialog.SaveConfirmVisibleForTest);   // ...and raises the save/cancel prompt

        Assert.True(dialog.PressEscapeForTest());        // Escape again backs out of the prompt
        Assert.False(dialog.SaveConfirmVisibleForTest);
    }

    [AvaloniaFact]
    public void Escape_without_changes_does_not_prompt()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        Assert.False(dialog.PressEscapeForTest());     // not intercepted — the dialog just cancels
        Assert.False(dialog.SaveConfirmVisibleForTest);

        dialog.SetSegmentCountForTest(5); // changing the count counts as a change too
        Assert.True(dialog.IsDirtyForTest);
        Assert.True(dialog.PressEscapeForTest());
        Assert.True(dialog.SaveConfirmVisibleForTest);
    }

    [AvaloniaFact]
    public void Clicking_a_wedge_opens_a_command_picker_that_binds_that_slot()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        dialog.OpenCommandPickerForTest(2);
        Assert.True(dialog.CommandPickerVisibleForTest);
        Assert.Equal(2, dialog.CommandPickerSegmentForTest);

        // Empty query lists the unbind row first, then the commands.
        var all = dialog.PickerResultIdsForTest(string.Empty);
        Assert.Null(all[0]); // "— None —"
        Assert.Contains("format.bold", all);

        // Fuzzy search narrows to matching commands.
        var bold = dialog.PickerResultIdsForTest("bold");
        Assert.Contains("format.bold", bold);
        Assert.DoesNotContain("edit.undo", bold);

        // Committing assigns the command to that wedge and closes the picker.
        dialog.CommitPickerForTest("format.bold");
        Assert.False(dialog.CommandPickerVisibleForTest);
        Assert.Equal("format.bold", dialog.BuildConfigForTest().CommandIdAt(2));
        Assert.Equal("edit.copy", dialog.BuildConfigForTest().CommandIdAt(1)); // neighbours untouched

        // Picking "— None —" unbinds the wedge.
        dialog.OpenCommandPickerForTest(2);
        dialog.CommitPickerForTest(null);
        Assert.Null(dialog.BuildConfigForTest().CommandIdAt(2));
    }

    [AvaloniaFact]
    public void Command_picker_dismisses_on_escape_without_binding()
    {
        var dialog = new RadialSettingsDialog(new RadialMenuConfig
        {
            SegmentCount = 4,
            CommandIds = new List<string?> { "edit.cut", "edit.copy", "edit.paste", "edit.undo" },
        });

        dialog.OpenCommandPickerForTest(1);
        Assert.True(dialog.CommandPickerVisibleForTest);

        Assert.True(dialog.PressEscapeForTest()); // Esc closes the picker first, not the dialog
        Assert.False(dialog.CommandPickerVisibleForTest);
        Assert.Equal("edit.copy", dialog.BuildConfigForTest().CommandIdAt(1)); // binding unchanged
    }

    [AvaloniaFact]
    public void Radial_overlay_floats_a_command_tooltip_over_the_active_wedge()
    {
        var overlay = new RadialMenuOverlay { FitToBounds = true };
        var window = new Window { Width = 280, Height = 280, Content = overlay };
        window.Show();

        overlay.Open(new Avalonia.Point(140, 140), new[]
        {
            new RadialSegment("edit.copy", "copy", "Copy", true),
            new RadialSegment("edit.paste", "clipboard", "Paste", true),
            new RadialSegment(null, null, string.Empty, false),
        });

        overlay.SetActiveSegment(0); // top wedge — tip anchored above its outer edge
        Assert.True(overlay.TooltipOpenForTest);
        Assert.Equal("edit.copy", overlay.ActiveTooltipCommandIdForTest);
        Assert.Equal(Avalonia.Controls.PlacementMode.Top, Avalonia.Controls.ToolTip.GetPlacement(overlay));

        overlay.SetActiveSegment(1); // wedge pointing lower-right — tip anchored to its right
        Assert.Equal(Avalonia.Controls.PlacementMode.Right, Avalonia.Controls.ToolTip.GetPlacement(overlay));

        overlay.SetActiveSegment(2); // an unbound wedge has no command — the tip closes
        Assert.False(overlay.TooltipOpenForTest);
        Assert.Null(overlay.ActiveTooltipCommandIdForTest);

        overlay.SetActiveSegment(-1); // back in the dead zone — still closed
        Assert.False(overlay.TooltipOpenForTest);

        window.AssertRenders(); // the popup + overlay render without throwing
    }

    [AvaloniaFact]
    public void Radial_tooltip_shows_only_for_a_real_hover_not_a_programmatic_highlight()
    {
        var overlay = new RadialMenuOverlay { FitToBounds = true };
        var window = new Window { Width = 280, Height = 280, Content = overlay };
        window.Show();
        overlay.Open(new Avalonia.Point(140, 140), new[]
        {
            new RadialSegment("edit.copy", "copy", "Copy", true),
            new RadialSegment("edit.paste", "clipboard", "Paste", true),
        });

        overlay.SetActiveSegment(0, showTooltip: false); // e.g. a keyboard selection on the settings list
        Assert.Equal(0, overlay.ActiveSegment);          // the wedge still highlights...
        Assert.False(overlay.TooltipOpenForTest);        // ...but no tip pops where the mouse isn't

        overlay.SetActiveSegment(0, showTooltip: true);  // now an actual hover over that wedge
        Assert.True(overlay.TooltipOpenForTest);

        overlay.SetActiveSegment(0, showTooltip: false); // hover leaves while it stays selected — tip closes
        Assert.False(overlay.TooltipOpenForTest);
    }

    [AvaloniaFact]
    public void Radial_tooltip_dismisses_when_the_overlay_leaves_the_tree()
    {
        var overlay = new RadialMenuOverlay { FitToBounds = true };
        var window = new Window { Width = 280, Height = 280, Content = overlay };
        window.Show();
        overlay.Open(new Avalonia.Point(140, 140), new[] { new RadialSegment("edit.copy", "copy", "Copy", true) });
        overlay.SetActiveSegment(0);
        Assert.True(overlay.TooltipOpenForTest);

        window.Content = null; // detaching the overlay must take its tooltip down with it
        Assert.False(overlay.TooltipOpenForTest);
    }
}
