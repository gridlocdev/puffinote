using System.Text;
using Cellestra.Core.Serialization.Ods;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Search;

/// <summary>
/// Builds and holds the notebook's full-text search index (PRD §7.2.3). Notes (<c>.md</c>/<c>.txt</c>) are
/// indexed as raw text; spreadsheets (<c>.ods</c>) by their cell contents via <see cref="OdsReader"/> (the
/// Core engine, no editor). Indexing runs off the UI thread and is non-blocking: <see cref="Search"/> always
/// answers against whatever is currently indexed, and <see cref="IndexUpdated"/> lets the panel repaint its
/// status/results when a (re)build finishes. In-memory for now (rebuilt on notebook open and after tree
/// mutations); an on-disk index in <c>.puffinote/</c> is a later optimization (§7.1).
/// </summary>
internal sealed class NotebookSearchService
{
    private readonly NotebookService _notebook;
    private volatile IReadOnlyList<IndexedFile> _index = Array.Empty<IndexedFile>();
    private int _generation;

    public NotebookSearchService(NotebookService notebook) => _notebook = notebook;

    /// <summary>Raised (on the calling thread — the UI thread in the app) when indexing starts and finishes.</summary>
    public event Action? IndexUpdated;

    /// <summary>True while a rebuild is in flight.</summary>
    public bool IsIndexing { get; private set; }

    /// <summary>The number of files currently indexed.</summary>
    public int FileCount => _index.Count;

    /// <summary>Rebuild the index from the current notebook contents, off the UI thread.</summary>
    public async Task RebuildAsync()
    {
        var generation = ++_generation;
        IsIndexing = true;
        IndexUpdated?.Invoke();

        var paths = _notebook.EnumerateFiles();
        var index = await Task.Run(() => BuildIndex(paths));

        if (generation != _generation)
        {
            return; // a newer rebuild superseded this one — let it publish its result
        }

        _index = index;
        IsIndexing = false;
        IndexUpdated?.Invoke();
    }

    /// <summary>Search the current index (case-insensitive substring, ranked by hit count).</summary>
    public IReadOnlyList<SearchHit> Search(string query) => SearchQuery.Run(_index, query, _notebook.RootPath);

    private static IReadOnlyList<IndexedFile> BuildIndex(IReadOnlyList<string> paths)
    {
        var files = new List<IndexedFile>();
        foreach (var path in paths)
        {
            if (ReadText(path) is { } text)
            {
                files.Add(new IndexedFile(path, text));
            }
        }

        return files;
    }

    // Extract searchable text for a file, or null to skip it (binary / unreadable / a workbook that won't
    // parse) — indexing degrades gracefully rather than throwing on one bad file.
    private static string? ReadText(string path)
    {
        try
        {
            return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".md" or ".markdown" or ".mdown" or ".txt" => File.ReadAllText(path),
                ".ods" => ReadOdsText(path),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            return null;
        }
    }

    private static string ReadOdsText(string path)
    {
        using var stream = File.OpenRead(path);
        var workbook = OdsReader.Read(stream);

        var sb = new StringBuilder();
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var (_, _, cell) in sheet.EnumerateCells())
            {
                if (!cell.Value.IsEmpty)
                {
                    sb.Append(cell.Value.ToInvariantString()).Append('\n');
                }
            }
        }

        return sb.ToString();
    }
}
