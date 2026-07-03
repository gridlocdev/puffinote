using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class NotebookTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-test");

    public void Dispose() => _notebook.Delete(recursive: true);

    private string Touch(string relative)
    {
        var path = Path.Combine(_notebook.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void EnumerateChildren_lists_directories_first_then_files_alphabetically()
    {
        Touch("notes.md");
        Touch("budget.ods");
        Directory.CreateDirectory(Path.Combine(_notebook.FullName, "Zed"));
        Directory.CreateDirectory(Path.Combine(_notebook.FullName, "alpha"));

        var names = new NotebookNode(_notebook.FullName, isDirectory: true)
            .EnumerateChildren()
            .Select(n => n.Name)
            .ToArray();

        // directories (alpha, case-insensitive) before files (alpha)
        Assert.Equal(new[] { "alpha", "Zed", "budget.ods", "notes.md" }, names);
    }

    [Fact]
    public void EnumerateChildren_skips_dotfiles_and_the_state_directory()
    {
        Touch("visible.md");
        Touch(".hidden");
        Directory.CreateDirectory(Path.Combine(_notebook.FullName, NotebookNode.StateDirectoryName));

        var names = new NotebookNode(_notebook.FullName, isDirectory: true)
            .EnumerateChildren()
            .Select(n => n.Name)
            .ToArray();

        Assert.Equal(new[] { "visible.md" }, names);
    }

    [Fact]
    public void EnumerateChildren_on_a_file_is_empty()
    {
        var file = Touch("notes.md");
        Assert.Empty(new NotebookNode(file, isDirectory: false).EnumerateChildren());
    }

    [Fact]
    public void OpenNotebook_sets_root_and_raises_the_event()
    {
        var service = new NotebookService();
        var raised = 0;
        service.NotebookOpened += (_, _) => raised++;

        Assert.True(service.OpenNotebook(_notebook.FullName));
        Assert.NotNull(service.Root);
        Assert.Equal(_notebook.FullName, service.RootPath);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void OpenNotebook_on_a_missing_folder_returns_false_and_does_not_raise()
    {
        var service = new NotebookService();
        var raised = 0;
        service.NotebookOpened += (_, _) => raised++;

        Assert.False(service.OpenNotebook(Path.Combine(_notebook.FullName, "does-not-exist")));
        Assert.Null(service.Root);
        Assert.Equal(0, raised);
    }
}
