using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class PathScopeTests
{
    // Use forward slashes; these are treated as ordinary absolute-ish paths by the string logic.
    private static string P(string s) => s.Replace('/', System.IO.Path.DirectorySeparatorChar);

    [Fact]
    public void Remap_renames_an_exact_file_match()
    {
        Assert.Equal(P("/v/new.md"), PathScope.Remap(P("/v/old.md"), P("/v/old.md"), P("/v/new.md")));
    }

    [Fact]
    public void Remap_reparents_a_file_under_a_renamed_folder()
    {
        Assert.Equal(
            P("/v/renamed/a/note.md"),
            PathScope.Remap(P("/v/sub/a/note.md"), P("/v/sub"), P("/v/renamed")));
    }

    [Fact]
    public void Remap_leaves_unrelated_paths_alone()
    {
        Assert.Null(PathScope.Remap(P("/v/other.md"), P("/v/sub"), P("/v/renamed")));
        // A sibling that merely shares a name prefix is not "under" the folder.
        Assert.Null(PathScope.Remap(P("/v/subway.md"), P("/v/sub"), P("/v/renamed")));
    }

    [Fact]
    public void IsUnderOrEqual_covers_the_path_itself_and_descendants_but_not_prefix_siblings()
    {
        Assert.True(PathScope.IsUnderOrEqual(P("/v/sub"), P("/v/sub")));
        Assert.True(PathScope.IsUnderOrEqual(P("/v/sub/a/note.md"), P("/v/sub")));
        Assert.False(PathScope.IsUnderOrEqual(P("/v/subway.md"), P("/v/sub")));
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        Assert.Equal(P("/v/NEW.md"), PathScope.Remap(P("/v/Old.md"), P("/v/old.MD"), P("/v/NEW.md")));
    }
}
