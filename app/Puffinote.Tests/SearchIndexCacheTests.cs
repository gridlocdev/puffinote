using System.Linq;
using Puffinote.Desktop.Search;
using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class SearchIndexCacheTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("puffinote-cache");

    public void Dispose() => _root.Delete(recursive: true);

    private string At(string relative) => System.IO.Path.Combine(_root.FullName, relative);

    [Fact]
    public void Save_then_load_round_trips_and_writes_a_gitignore()
    {
        var index = new[]
        {
            new IndexedFile(At("note.md"), "hello world", 111, 11),
            new IndexedFile(At(System.IO.Path.Combine("sub", "deep.md")), "nested", 222, 6),
        };

        SearchIndexCache.Save(_root.FullName, index);

        // Volatile state lands in .puffinote/, ignored by git but leaving durable settings committable.
        var stateDir = System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName);
        Assert.True(File.Exists(System.IO.Path.Combine(stateDir, "search-index.json")));
        Assert.Contains("search-index.json", File.ReadAllText(System.IO.Path.Combine(stateDir, ".gitignore")));

        var loaded = SearchIndexCache.Load(_root.FullName).OrderBy(f => f.Path).ToList();
        Assert.Equal(2, loaded.Count);
        var note = loaded.Single(f => f.Path == At("note.md"));
        Assert.Equal("hello world", note.Text);
        Assert.Equal(111, note.MTimeTicks);
        Assert.Equal(11, note.Size);
        Assert.Equal(At(System.IO.Path.Combine("sub", "deep.md")), loaded.Single(f => f.Text == "nested").Path);
    }

    [Fact]
    public void Load_returns_empty_when_the_cache_is_absent_or_corrupt()
    {
        Assert.Empty(SearchIndexCache.Load(_root.FullName)); // nothing written yet

        var stateDir = Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName));
        File.WriteAllText(System.IO.Path.Combine(stateDir.FullName, "search-index.json"), "{ not valid json");
        Assert.Empty(SearchIndexCache.Load(_root.FullName)); // corrupt → empty, not a throw
    }
}
