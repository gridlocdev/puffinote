using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class NotebookWatcherTests
{
    private const string Root = "/notebook";

    private static string Under(params string[] segments) =>
        System.IO.Path.Combine([Root, .. segments]);

    [Fact]
    public void Ordinary_files_in_the_notebook_are_relevant()
    {
        Assert.True(NotebookWatcher.IsRelevant(Root, Under("note.md")));
        Assert.True(NotebookWatcher.IsRelevant(Root, Under("sub", "deep", "book.ods")));
    }

    [Fact]
    public void The_state_directory_and_dot_entries_are_ignored()
    {
        // Our own index writes live here — ignoring them avoids a refresh feedback loop.
        Assert.False(NotebookWatcher.IsRelevant(Root, Under(".puffinote", "search-index.json")));
        Assert.False(NotebookWatcher.IsRelevant(Root, Under(".hidden")));
        Assert.False(NotebookWatcher.IsRelevant(Root, Under(".git", "config")));
        Assert.False(NotebookWatcher.IsRelevant(Root, Under("sub", ".secret", "x.md")));
    }

    [Fact]
    public void The_root_itself_and_paths_outside_it_are_ignored()
    {
        Assert.False(NotebookWatcher.IsRelevant(Root, Root));
        Assert.False(NotebookWatcher.IsRelevant(Root, "/elsewhere/note.md"));
    }
}
