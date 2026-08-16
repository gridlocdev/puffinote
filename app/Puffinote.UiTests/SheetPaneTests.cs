using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Services;
using Cellestra.Editor;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
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

    [AvaloniaFact]
    public async Task A_newly_created_blank_spreadsheet_opens_as_a_real_editor()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-new-ods");
        try
        {
            // Exactly what "New Spreadsheet" writes: a blank-workbook image, not an empty file.
            var path = NotebookMutations.CreateFile(
                dir.FullName, "book.ods", WorkbookSession.NewBlank().SerializeToBytes());
            var host = new TabHost();

            await host.OpenAsync(path);

            Assert.Equal(1, host.Count);
            Assert.IsType<SheetPane>(host.ActivePane);
            Assert.Equal("book.ods", host.ActivePane!.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Opening_an_empty_ods_shows_an_error_tab_instead_of_a_blank_editor()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-empty-ods");
        try
        {
            // An empty (0-byte) .ods — exactly what the old "New File" produced — isn't a valid workbook, so
            // LoadAndRecalc throws. Rather than a blank editor, no tab, or an unobserved crash, the editor
            // area shows the reason in an ErrorPane.
            var empty = Path.Combine(dir.FullName, "broken.ods");
            File.WriteAllBytes(empty, []);
            var host = new TabHost();

            await host.OpenAsync(empty);

            Assert.Equal(1, host.Count);
            var pane = Assert.IsType<ErrorPane>(host.ActivePane);
            Assert.Equal("broken.ods", pane.Title);
            // Selectable, so the reason can be copied out of the tab rather than retyped from it.
            Assert.Contains("broken.ods", Assert.IsType<SelectableTextBlock>(pane.View).Text);
            Assert.Null(pane.Path); // an error tab is never recorded as recent
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
