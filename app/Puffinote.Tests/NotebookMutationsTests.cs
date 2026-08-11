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

    // ---- Move (the tree's drag-and-drop) ----------------------------------------------------------

    [Fact]
    public void Move_puts_a_file_in_the_target_folder_keeping_its_name()
    {
        var file = NotebookMutations.CreateFile(_dir.FullName, "note.md", "body"u8.ToArray());
        var folder = NotebookMutations.CreateFolder(_dir.FullName, "archive");

        var moved = NotebookMutations.Move(file, folder);

        Assert.Equal(Path(System.IO.Path.Combine("archive", "note.md")), moved);
        Assert.False(File.Exists(file));
        Assert.Equal("body", File.ReadAllText(moved));
    }

    [Fact]
    public void Move_takes_a_folder_and_everything_in_it()
    {
        var source = NotebookMutations.CreateFolder(_dir.FullName, "src");
        NotebookMutations.CreateFile(source, "child.md");
        var target = NotebookMutations.CreateFolder(_dir.FullName, "dest");

        var moved = NotebookMutations.Move(source, target);

        Assert.True(File.Exists(System.IO.Path.Combine(moved, "child.md")));
        Assert.False(Directory.Exists(source));
    }

    [Fact]
    public void Move_refuses_a_name_already_taken_there_rather_than_overwriting_it()
    {
        var file = NotebookMutations.CreateFile(_dir.FullName, "note.md");
        var folder = NotebookMutations.CreateFolder(_dir.FullName, "archive");
        NotebookMutations.CreateFile(folder, "note.md", "the one already there"u8.ToArray());

        Assert.Throws<IOException>(() => NotebookMutations.Move(file, folder));
        Assert.Equal("the one already there", File.ReadAllText(System.IO.Path.Combine(folder, "note.md")));
    }

    [Fact]
    public void CanMove_refuses_the_three_moves_that_make_no_sense()
    {
        var folder = NotebookMutations.CreateFolder(_dir.FullName, "outer");
        var inner = NotebookMutations.CreateFolder(folder, "inner");
        var file = NotebookMutations.CreateFile(_dir.FullName, "note.md");

        Assert.False(NotebookMutations.CanMove(folder, folder));         // into itself
        Assert.False(NotebookMutations.CanMove(folder, inner));          // into its own descendant
        Assert.False(NotebookMutations.CanMove(file, _dir.FullName));    // where it already is

        Assert.True(NotebookMutations.CanMove(file, folder));
        Assert.True(NotebookMutations.CanMove(inner, _dir.FullName));    // out to the top level
        Assert.Throws<IOException>(() => NotebookMutations.Move(folder, inner));
    }
}
