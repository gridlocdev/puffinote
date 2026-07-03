using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class NotebookPathsTests
{
    private static string Abs(params string[] parts) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(parts));

    [Fact]
    public void RelativeTo_renders_a_child_relative_to_the_root()
    {
        var root = Abs("v", "notes");
        Assert.Equal("sub" + System.IO.Path.DirectorySeparatorChar + "deep.md",
            NotebookPaths.RelativeTo(root, System.IO.Path.Combine(root, "sub", "deep.md")));
    }

    [Fact]
    public void RelativeTo_does_not_treat_a_sibling_sharing_the_prefix_as_under_the_root()
    {
        var root = Abs("v", "notes");
        var sibling = Abs("v", "notes-archive", "x.md"); // shares the "notes" prefix but is NOT under it

        Assert.Equal(sibling, NotebookPaths.RelativeTo(root, sibling)); // returned unchanged, not "-archive/x.md"
    }

    [Fact]
    public void RelativeTo_returns_empty_for_the_root_itself_and_the_path_when_no_root()
    {
        var root = Abs("v", "notes");
        Assert.Equal(string.Empty, NotebookPaths.RelativeTo(root, root));
        Assert.Equal("/anything", NotebookPaths.RelativeTo(null, "/anything"));
    }
}
