using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class NotebookMutationsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-mut");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Path(string relative) => System.IO.Path.Combine(_dir.FullName, relative);

    [Fact]
    public void CreateFile_makes_an_empty_file_and_returns_its_path()
    {
        var path = NotebookMutations.CreateFile(_dir.FullName, "note.md");

        Assert.Equal(Path("note.md"), path);
        Assert.True(File.Exists(path));
        Assert.Equal(string.Empty, File.ReadAllText(path));
    }

    [Fact]
    public void CreateFile_writes_the_given_initial_content()
    {
        // "New Spreadsheet" seeds a blank-workbook image here, so an .ods is a valid workbook (not 0 bytes).
        var content = new byte[] { 1, 2, 3, 4 };
        var path = NotebookMutations.CreateFile(_dir.FullName, "book.ods", content);

        Assert.Equal(content, File.ReadAllBytes(path));
    }

    [Fact]
    public void CreateFolder_makes_a_directory()
    {
        var path = NotebookMutations.CreateFolder(_dir.FullName, "sub");
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Create_rejects_a_name_that_already_exists()
    {
        NotebookMutations.CreateFile(_dir.FullName, "dup.md");
        Assert.Throws<IOException>(() => NotebookMutations.CreateFile(_dir.FullName, "dup.md"));
        Assert.Throws<IOException>(() => NotebookMutations.CreateFolder(_dir.FullName, "dup.md"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Create_rejects_invalid_names(string name) =>
        Assert.Throws<IOException>(() => NotebookMutations.CreateFile(_dir.FullName, name));

    [Fact]
    public void Rename_moves_the_entry_and_returns_the_new_path()
    {
        var original = NotebookMutations.CreateFile(_dir.FullName, "old.md");

        var renamed = NotebookMutations.Rename(original, "new.md");

        Assert.Equal(Path("new.md"), renamed);
        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public void Rename_to_an_existing_name_throws_and_leaves_the_original()
    {
        var original = NotebookMutations.CreateFile(_dir.FullName, "keep.md");
        NotebookMutations.CreateFile(_dir.FullName, "taken.md");

        Assert.Throws<IOException>(() => NotebookMutations.Rename(original, "taken.md"));
        Assert.True(File.Exists(original));
    }

    [Fact]
    public void Rename_can_change_only_the_letter_case()
    {
        var original = NotebookMutations.CreateFile(_dir.FullName, "Note.md");

        // A case-only change must be allowed — on a case-insensitive filesystem the target "exists" as the
        // same file, and it must not be rejected as a collision (C1).
        var renamed = NotebookMutations.Rename(original, "NOTE.md");

        Assert.Equal(Path("NOTE.md"), renamed);
        Assert.True(File.Exists(renamed));
    }

    [Fact]
    public void Rename_to_the_same_name_is_a_no_op()
    {
        var original = NotebookMutations.CreateFile(_dir.FullName, "same.md");
        Assert.Equal(original, NotebookMutations.Rename(original, "same.md"));
        Assert.True(File.Exists(original));
    }

    [Fact]
    public void Delete_removes_a_file()
    {
        var path = NotebookMutations.CreateFile(_dir.FullName, "gone.md");
        NotebookMutations.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_removes_a_folder_recursively()
    {
        var folder = NotebookMutations.CreateFolder(_dir.FullName, "tree");
        NotebookMutations.CreateFile(folder, "child.md");

        NotebookMutations.Delete(folder);

        Assert.False(Directory.Exists(folder));
    }
}
