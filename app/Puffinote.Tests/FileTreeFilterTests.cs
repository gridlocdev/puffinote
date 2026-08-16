using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The rule behind the sidebar's Filter panel (PRD §7.2.3): which entries a typed query keeps, which folders
/// it opens on the way to them, and how many of them it calls matches. Pure filesystem logic, so it is held
/// here rather than through a window.
/// </summary>
public sealed class FileTreeFilterTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-filter");

    public void Dispose() => _notebook.Delete(recursive: true);

    private string Path(string relative) => System.IO.Path.Combine(_notebook.FullName, relative);

    private void Touch(string relative)
    {
        var path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    /// <summary>root/{ Projects/{ Roadmap.md, Budget.ods, Neural Nets/Attention.md }, Inbox.md, notes.txt }</summary>
    private NotebookNode Notebook()
    {
        Touch("Projects/Roadmap.md");
        Touch("Projects/Budget.ods");
        Touch("Projects/Neural Nets/Attention.md");
        Touch("Inbox.md");
        Touch("notes.txt");
        return new NotebookNode(_notebook.FullName, isDirectory: true);
    }

    private FileTreeFilter Build(string query) => Assert.IsType<FileTreeFilter>(FileTreeFilter.Build(Notebook(), query));

    [Fact]
    public void A_blank_query_is_no_filter_at_all()
    {
        // Not "match nothing": an empty box means the panel has nothing to say about the notebook yet, and a
        // tree emptied by whitespace would read as a notebook that had lost its files.
        Assert.Null(FileTreeFilter.Build(Notebook(), string.Empty));
        Assert.Null(FileTreeFilter.Build(Notebook(), "   "));
        Assert.Null(FileTreeFilter.Build(root: null, "roadmap"));
    }

    [Fact]
    public void It_keeps_the_matches_and_the_folders_above_them()
    {
        var filter = Build("attention");

        Assert.True(filter.Keeps(Path("Projects/Neural Nets/Attention.md")));

        // The two folders it lives in are kept as well — a match four levels down still has to be shown
        // where it lives — and both are opened, so it lands on screen rather than behind two chevrons.
        Assert.True(filter.Keeps(Path("Projects")));
        Assert.True(filter.Keeps(Path("Projects/Neural Nets")));
        Assert.Contains(Path("Projects"), filter.FoldersToOpen);
        Assert.Contains(Path("Projects/Neural Nets"), filter.FoldersToOpen);

        // Nothing else survives, and the count is the one entry that actually matched — not the folders
        // carried along to hold it.
        Assert.False(filter.Keeps(Path("Inbox.md")));
        Assert.False(filter.Keeps(Path("Projects/Roadmap.md")));
        Assert.Equal(1, filter.MatchCount);
    }

    [Fact]
    public void An_extension_is_part_of_the_name_you_can_type()
    {
        // The whole point of typing "md": the tree may be drawing bare stems (the ShowExtensions preference),
        // but a file is still called what it is called.
        var filter = Build("md");

        Assert.True(filter.Keeps(Path("Inbox.md")));
        Assert.True(filter.Keeps(Path("Projects/Roadmap.md")));
        Assert.True(filter.Keeps(Path("Projects/Neural Nets/Attention.md")));
        Assert.False(filter.Keeps(Path("notes.txt")));
        Assert.False(filter.Keeps(Path("Projects/Budget.ods")));
        Assert.Equal(3, filter.MatchCount);
    }

    [Fact]
    public void Case_and_position_dont_matter()
    {
        // Case-insensitive, and a substring rather than a prefix — "oadma" finds Roadmap.md, which is what
        // makes the box usable when you remember the middle of a name and not its start.
        var filter = Build("OADMA");

        Assert.True(filter.Keeps(Path("Projects/Roadmap.md")));
        Assert.Equal(1, filter.MatchCount);
    }

    [Fact]
    public void A_matching_folder_brings_its_contents_with_it()
    {
        // A folder in a file tree *is* its contents. Kept as a leaf you cannot open, a folder you searched
        // for by name would be the one result the panel refuses to show you.
        var filter = Build("projects");

        Assert.True(filter.Keeps(Path("Projects")));
        Assert.True(filter.Keeps(Path("Projects/Roadmap.md")));
        Assert.True(filter.Keeps(Path("Projects/Neural Nets")));
        Assert.True(filter.Keeps(Path("Projects/Neural Nets/Attention.md")));
        Assert.False(filter.Keeps(Path("Inbox.md")));

        // The folder itself matched, once — its contents came along, they were not each a hit. And it is not
        // force-opened: it is a whole subtree, and unrolling it on a keystroke is a lot to do to someone who
        // typed a folder's name.
        Assert.Equal(1, filter.MatchCount);
        Assert.DoesNotContain(Path("Projects"), filter.FoldersToOpen);
    }

    [Fact]
    public void A_match_inside_a_matching_folder_is_still_counted_and_still_opened()
    {
        // "e" hits Projects, Neural Nets and Attention.md — the sweep inside a matched folder must keep
        // walking, or the deeper matches would arrive uncounted and behind shut folders.
        var filter = Build("neural");
        Assert.Contains(Path("Projects"), filter.FoldersToOpen); // opened: the match is below it
        Assert.Equal(1, filter.MatchCount);

        var nested = Build("attention");
        Assert.Contains(Path("Projects/Neural Nets"), nested.FoldersToOpen);
    }

    [Fact]
    public void Nothing_matching_keeps_nothing()
    {
        var filter = Build("zzz");

        Assert.Equal(0, filter.MatchCount);
        Assert.False(filter.Keeps(Path("Inbox.md")));
        Assert.False(filter.Keeps(Path("Projects")));
        Assert.Empty(filter.FoldersToOpen);
    }

    [Theory]
    [InlineData("Roadmap.md", "road", true)]
    [InlineData("Roadmap.md", "ROAD", true)]
    [InlineData("Roadmap.md", ".md", true)]
    [InlineData("Roadmap.md", "map.md", true)]
    [InlineData("Roadmap.md", "rdmp", false)] // a filter, not the fuzzy matcher behind ⌘O
    [InlineData("Roadmap.md", "", true)]
    public void Matches_is_a_case_insensitive_substring_of_the_whole_name(string name, string query, bool expected) =>
        Assert.Equal(expected, FileTreeFilter.Matches(name, query));
}
