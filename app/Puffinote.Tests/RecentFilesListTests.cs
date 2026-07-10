using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class RecentFilesListTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("puffinote-recent");

    public void Dispose() => _root.Delete(recursive: true);

    private string Touch(string relative)
    {
        var path = System.IO.Path.Combine(_root.FullName, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string At(string relative) => System.IO.Path.Combine(_root.FullName, relative);

    [Fact]
    public void Load_keeps_only_paths_whose_file_still_exists()
    {
        var present = Touch("note.md");
        var missing = At("gone.md");

        var recent = new RecentFilesList();
        recent.Load([present, missing]);

        Assert.Equal([present], recent.Paths);
    }

    [Fact]
    public void Push_promotes_to_most_recent_and_de_duplicates()
    {
        var recent = new RecentFilesList();
        recent.Push(At("a.md"));
        recent.Push(At("b.md"));
        recent.Push(At("a.md")); // re-open a.md — it moves to the front, not duplicated

        Assert.Equal([At("a.md"), At("b.md")], recent.Paths);
    }

    [Fact]
    public void Remap_rewrites_entries_under_a_renamed_folder_and_reports_the_change()
    {
        var recent = new RecentFilesList();
        recent.Load([]);
        recent.Push(At(System.IO.Path.Combine("Projects", "plan.md")));
        recent.Push(At("top.md"));

        var changed = recent.Remap(At("Projects"), At("Archive"));

        Assert.True(changed);
        Assert.Equal(
            [At("top.md"), At(System.IO.Path.Combine("Archive", "plan.md"))],
            recent.Paths);
    }

    [Fact]
    public void Remap_reports_no_change_when_nothing_matches()
    {
        var recent = new RecentFilesList();
        recent.Push(At("top.md"));

        Assert.False(recent.Remap(At("Projects"), At("Archive")));
        Assert.Equal([At("top.md")], recent.Paths);
    }

    [Fact]
    public void DropUnder_removes_a_deleted_file_and_everything_under_a_deleted_folder()
    {
        var recent = new RecentFilesList();
        recent.Push(At(System.IO.Path.Combine("Projects", "plan.md")));
        recent.Push(At(System.IO.Path.Combine("Projects", "notes.md")));
        recent.Push(At("top.md"));

        Assert.True(recent.DropUnder(At("Projects")));
        Assert.Equal([At("top.md")], recent.Paths);

        Assert.True(recent.DropUnder(At("top.md")));
        Assert.Empty(recent.Paths);
    }

    [Fact]
    public void DropUnder_reports_no_change_when_the_target_is_not_in_the_list()
    {
        var recent = new RecentFilesList();
        recent.Push(At("top.md"));

        Assert.False(recent.DropUnder(At("elsewhere.md")));
        Assert.Equal([At("top.md")], recent.Paths);
    }
}
