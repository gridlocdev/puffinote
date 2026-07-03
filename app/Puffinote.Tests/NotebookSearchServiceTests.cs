using Cellestra.Core.Values;
using Cellestra.Desktop.Services;
using Puffinote.Desktop.Search;
using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class NotebookSearchServiceTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-search");

    public void Dispose() => _notebook.Delete(recursive: true);

    private string Path(string relative) => System.IO.Path.Combine(_notebook.FullName, relative);

    private void WriteOds(string relative, params string[] cellTexts)
    {
        var session = WorkbookSession.NewBlank();
        for (var row = 0; row < cellTexts.Length; row++)
        {
            session.ActiveSheet.SetValue(row, 0, CellValue.Text(cellTexts[row]));
        }

        File.WriteAllBytes(Path(relative), session.SerializeToBytes());
    }

    [Fact]
    public async Task Indexes_notes_and_spreadsheet_cells_and_searches_both()
    {
        File.WriteAllText(Path("note.md"), "meeting notes about widgets");
        WriteOds("book.ods", "widgets", "revenue");
        File.WriteAllBytes(Path("image.png"), new byte[] { 1, 2, 3, 4 }); // binary → skipped

        var notebook = new NotebookService();
        notebook.OpenNotebook(_notebook.FullName);
        var search = new NotebookSearchService(notebook);

        await search.RebuildAsync();

        Assert.False(search.IsIndexing);
        Assert.Equal(2, search.FileCount); // note.md + book.ods (png not indexed)

        // "widgets" is in both the note and a cell; "revenue" only in the workbook.
        Assert.Equal(2, search.Search("widgets").Count);
        Assert.Single(search.Search("revenue"));
        Assert.Empty(search.Search("nonexistent"));
    }

    [Fact]
    public async Task An_unreadable_workbook_is_skipped_rather_than_throwing()
    {
        File.WriteAllText(Path("good.md"), "hello");
        File.WriteAllBytes(Path("broken.ods"), new byte[] { 0, 1, 2, 3 }); // not a real .ods

        var notebook = new NotebookService();
        notebook.OpenNotebook(_notebook.FullName);
        var search = new NotebookSearchService(notebook);

        await search.RebuildAsync();

        Assert.Equal(1, search.FileCount); // only the note; the corrupt workbook is dropped
        Assert.Single(search.Search("hello"));
    }
}
