using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// A node's display name. This is what the sidebar header shows, so getting it wrong is visible on every
/// screen — and it <i>was</i> wrong for any path that happened to end in a separator.
/// </summary>
public sealed class NotebookNodeNameTests
{
    private static readonly char Sep = System.IO.Path.DirectorySeparatorChar;

    [Fact]
    public void ATrailingSeparator_DoesNotTurnTheNameIntoTheWholePath()
    {
        // The bug: Path.GetFileName("/Users/ada/notes/") returns the empty string, not "notes", so the name fell
        // through to its "no file name" fallback and took the entire absolute path. That is why the sidebar
        // header read "/USERS/ADA/DOWNLOADS/TEST" instead of the folder's name.
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine("v", "notes"));

        var withSlash = new NotebookNode(root + Sep, isDirectory: true);
        var withoutSlash = new NotebookNode(root, isDirectory: true);

        Assert.Equal("notes", withSlash.Name);
        Assert.Equal(withoutSlash.Name, withSlash.Name); // the slash must make no difference at all
    }

    [Fact]
    public void APathWithNoFileName_StillFallsBackToThePath()
    {
        // A true filesystem root has no name even after trimming; it must still show *something* rather than "".
        var node = new NotebookNode(Sep.ToString(), isDirectory: true);
        Assert.False(string.IsNullOrEmpty(node.Name));
    }

    [Fact]
    public void AFileKeepsItsNameAndExtension()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine("v", "notes", "My Note.md"));
        Assert.Equal("My Note.md", new NotebookNode(path, isDirectory: false).Name);
    }
}
