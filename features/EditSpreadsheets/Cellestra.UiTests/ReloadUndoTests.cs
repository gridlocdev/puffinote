using System.Globalization;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;
using Cellestra.Desktop.Services;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Taking the version on disk after something else wrote to the open workbook is a change like any other,
/// so there is a way back from it. A reload cannot be an <c>IEditCommand</c> — it replaces the workbook,
/// its recalculation graph and its history at once — so the step back is the whole previous session state,
/// taken once the reloaded workbook's own history runs out.
/// </summary>
public class ReloadUndoTests
{
    private static WorkbookSession.LoadedWorkbook Load(string a1)
    {
        var source = Workbook.CreateNew();
        source.ActiveSheet.SetValue(0, 0, CellValue.Text(a1));
        using var buffer = new MemoryStream();
        OdsWriter.Write(source, buffer, null);
        buffer.Position = 0;
        return WorkbookSession.LoadAndRecalc(buffer);
    }

    [Fact]
    public void Undo_after_a_reload_gives_back_the_workbook_that_was_open()
    {
        var session = WorkbookSession.NewBlank();
        session.AdoptWorkbook(Load("mine"), "book.ods");
        session.Execute(EditCommands.SetInput(session.ActiveSheet.Name, 0, 1, "and mine", CultureInfo.InvariantCulture));

        session.AdoptWorkbook(Load("theirs"), "book.ods", reloading: true);
        Assert.Equal("theirs", session.ActiveSheet.GetValue(0, 0).AsText);
        Assert.False(session.IsModified);
        Assert.True(session.CanUndo);
        Assert.Equal("Reload", session.UndoLabel);

        session.Undo();
        Assert.Equal("mine", session.ActiveSheet.GetValue(0, 0).AsText);
        Assert.Equal("and mine", session.ActiveSheet.GetValue(0, 1).AsText); // the edit came back with it
        Assert.True(session.IsModified); // it differs from the file again, which it does

        // …and the workbook that came back brought its own history, so the edits before the reload still undo.
        Assert.True(session.CanUndo);
        session.Undo();
        Assert.True(session.ActiveSheet.GetValue(0, 1).IsEmpty);

        session.Redo();
        Assert.Equal("and mine", session.ActiveSheet.GetValue(0, 1).AsText);
        session.Redo(); // forward across the reload again
        Assert.Equal("theirs", session.ActiveSheet.GetValue(0, 0).AsText);
    }

    [Fact]
    public void Opening_a_different_document_is_not_a_step_back_to_the_last_one()
    {
        var session = WorkbookSession.NewBlank();
        session.AdoptWorkbook(Load("first"), "first.ods");
        session.AdoptWorkbook(Load("second"), "second.ods"); // an ordinary open, not a reload

        Assert.False(session.CanUndo);
        Assert.Equal("second", session.ActiveSheet.GetValue(0, 0).AsText);
    }
}
