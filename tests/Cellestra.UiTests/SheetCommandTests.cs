using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Services;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class SheetCommandTests
{
    [Fact]
    public void Duplicate_sheet_inserts_a_copy_after_it_and_activates_it()
    {
        var session = WorkbookSession.NewBlank();
        var source = session.ActiveSheet;
        source.SetValue(0, 0, CellValue.Text("data"));

        var copy = session.DuplicateSheet(source);

        Assert.Equal(2, session.Workbook.Worksheets.Count);
        Assert.Same(copy, session.ActiveSheet);                 // the copy becomes active
        Assert.Equal("Sheet1 copy", copy.Name);
        Assert.Equal("data", copy.GetValue(0, 0).AsText);       // content carried over
        Assert.Equal(1, session.Workbook.IndexOf(copy));        // sits right after the original
        Assert.True(session.IsModified);
    }

    [Fact]
    public void Delete_sheet_removes_it_activates_a_neighbour_and_refuses_the_last()
    {
        var session = WorkbookSession.NewBlank();
        var first = session.ActiveSheet;

        session.DeleteSheet(first); // refused — a workbook keeps at least one sheet
        Assert.Single(session.Workbook.Worksheets);

        var second = session.AddSheet();
        session.DeleteSheet(first); // now allowed
        Assert.Single(session.Workbook.Worksheets);
        Assert.Same(second, session.ActiveSheet);
        Assert.True(session.IsModified);
    }

    [Fact]
    public void Rename_sheet_changes_the_name_and_rejects_a_duplicate()
    {
        var session = WorkbookSession.NewBlank();
        var first = session.ActiveSheet;
        var second = session.AddSheet();

        session.RenameSheet(first, "Budget");
        Assert.Equal("Budget", first.Name);

        Assert.Throws<InvalidOperationException>(() => session.RenameSheet(second, "Budget"));
    }

    [Fact]
    public void Deleting_a_sheet_clears_undo_history_so_undo_cannot_replay_a_missing_sheet()
    {
        var session = WorkbookSession.NewBlank();
        var first = session.ActiveSheet;
        session.AddSheet();
        session.SetActiveSheet(first);
        session.Execute(EditCommands.SetValue(first.Name, 0, 0, CellValue.Number(1)));
        Assert.True(session.CanUndo);

        session.DeleteSheet(first);

        Assert.False(session.CanUndo); // history cleared — undoing would otherwise hit the missing sheet
        session.Undo();                // no-op, must not throw
    }

    [AvaloniaFact]
    public void Delete_sheet_command_is_disabled_until_a_second_sheet_exists()
    {
        var window = new MainWindow();
        window.Show();
        var delete = AppCommands.ById("sheet.delete")!;

        Assert.False(delete.IsEnabled(window.Editor)); // one sheet
        window.Editor.AddSheet();
        Assert.True(delete.IsEnabled(window.Editor));
    }

    [Fact]
    public void Move_sheet_reorders_the_tabs_and_keeps_the_active_sheet()
    {
        var session = WorkbookSession.NewBlank();
        var a = session.ActiveSheet;
        var b = session.AddSheet();
        var c = session.AddSheet(); // order a,b,c — c active

        session.MoveSheet(0, 2); // drag the first tab to the end

        Assert.Equal(new[] { b.Name, c.Name, a.Name }, session.Workbook.Worksheets.Select(w => w.Name).ToArray());
        Assert.Same(c, session.ActiveSheet); // the active sheet is preserved across a reorder
    }

    [AvaloniaFact]
    public void Delete_rows_command_removes_the_selected_rows()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Session.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Text("a")));
        env.Session.Execute(EditCommands.SetValue(sheet.Name, 1, 0, CellValue.Text("b")));
        env.Selection.MoveTo(0, 0);

        AppCommands.ById("edit.deleteRows")!.Execute(window.Editor);

        Assert.Equal("b", sheet.GetValue(0, 0).AsText); // row 0 deleted, "b" shifted up
    }

    [AvaloniaFact]
    public void Delete_columns_refuses_to_remove_every_column()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        env.Session.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Text("keep")));

        env.Selection.SetRange(0, 0, SheetDimensions.MaxRows - 1, SheetDimensions.MaxColumns - 1); // Select All
        AppCommands.ById("edit.deleteColumns")!.Execute(window.Editor);

        Assert.Equal("keep", sheet.GetValue(0, 0).AsText); // refused — deleting every column would wipe the sheet
    }
}
