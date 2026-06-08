using System;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Services;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class SheetCommandTests
{
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

        Assert.False(delete.IsEnabled(window)); // one sheet
        window.AddSheet();
        Assert.True(delete.IsEnabled(window));
    }
}
