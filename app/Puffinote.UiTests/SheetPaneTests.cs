using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Services;
using Cellestra.Editor;
using Puffinote.Desktop.Panes;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 2A: the embedded Cellestra spreadsheet — open → edit → save one .ods, composed unchanged.</summary>
public sealed class SheetPaneTests
{
    private static string WriteBlankOds(string path)
    {
        // Generate a real .ods from a blank workbook so the test needs no binary fixture on disk.
        File.WriteAllBytes(path, WorkbookSession.NewBlank().SerializeToBytes());
        return path;
    }

    [AvaloniaFact]
    public async Task Open_edit_save_round_trips_through_the_embedded_spreadsheet()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-sheet");
        try
        {
            var path = WriteBlankOds(Path.Combine(dir.FullName, "book.ods"));

            var pane = Assert.IsType<SheetPane>(EditorPaneFactory.Create(path));
            await pane.LoadAsync(path);
            Assert.False(pane.IsDirty);       // freshly opened → clean
            Assert.Equal("book.ods", pane.Title);

            // Make a structural edit through the embedded editor's session, then save.
            var view = Assert.IsType<SpreadsheetEditorView>(pane.View);
            view.Session.RenameSheet(view.Session.ActiveSheet, "Renamed");
            Assert.True(pane.IsDirty);

            await pane.SaveAsync();
            Assert.False(pane.IsDirty);

            // Reopen the bytes on disk and confirm the edit persisted.
            await using var reread = File.OpenRead(path);
            var reloaded = WorkbookSession.LoadAndRecalc(reread);
            Assert.Contains(reloaded.Workbook.Worksheets, ws => ws.Name == "Renamed");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Factory_routes_ods_to_a_sheet_pane_and_md_to_a_markdown_pane()
    {
        Assert.True(EditorPaneFactory.CanOpen("/x/budget.ODS"));
        Assert.IsType<SheetPane>(EditorPaneFactory.Create("/x/budget.ods"));
        Assert.IsType<MarkdownPane>(EditorPaneFactory.Create("/x/note.md"));
    }
}
