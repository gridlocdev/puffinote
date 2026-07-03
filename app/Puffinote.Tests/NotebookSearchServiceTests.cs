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
    public async Task Persists_the_index_to_disk_and_a_fresh_service_reuses_it()
    {
        File.WriteAllText(Path("note.md"), "widgets everywhere");
        WriteOds("book.ods", "widgets");

        var notebook = new NotebookService();
        notebook.OpenNotebook(_notebook.FullName);

        var first = new NotebookSearchService(notebook);
        await first.RebuildAsync();

        var cache = System.IO.Path.Combine(_notebook.FullName, NotebookNode.StateDirectoryName, "search-index.json");
        Assert.True(File.Exists(cache)); // the index was persisted for the next open

        // A brand-new service on the same notebook seeds from the on-disk cache and answers the same.
        var reopened = new NotebookSearchService(notebook);
        await reopened.RebuildAsync();
        Assert.Equal(2, reopened.FileCount);
        Assert.Equal(2, reopened.Search("widgets").Count);
    }

    [Fact]
    public async Task An_unchanged_rebuild_does_not_rewrite_the_index_cache()
    {
        File.WriteAllText(Path("note.md"), "widgets");
        var notebook = new NotebookService();
        notebook.OpenNotebook(_notebook.FullName);
        var search = new NotebookSearchService(notebook);

        await search.RebuildAsync(); // first build writes the cache
        var cache = System.IO.Path.Combine(_notebook.FullName, NotebookNode.StateDirectoryName, "search-index.json");
        Assert.True(File.Exists(cache));

        File.Delete(cache);
        await search.RebuildAsync(); // nothing changed → the cache must not be rewritten (P2)

        Assert.False(File.Exists(cache));
    }

    [Fact]
    public async Task Rebuilding_drops_deleted_files_and_picks_up_changed_ones()
    {
        var note = Path("note.md");
        File.WriteAllText(note, "alpha");

        var notebook = new NotebookService();
        notebook.OpenNotebook(_notebook.FullName);
        var search = new NotebookSearchService(notebook);

        await search.RebuildAsync();
        Assert.Single(search.Search("alpha"));

        File.WriteAllText(note, "beta gamma"); // content (and size) changed → re-read on rebuild
        await search.RebuildAsync();
        Assert.Empty(search.Search("alpha"));
        Assert.Single(search.Search("beta"));

        File.Delete(note);
        await search.RebuildAsync();
        Assert.Equal(0, search.FileCount);
        Assert.Empty(search.Search("beta"));
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
