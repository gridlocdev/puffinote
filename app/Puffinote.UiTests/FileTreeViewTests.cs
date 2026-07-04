using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 4B-1: the keyboard-operable custom file tree (PRD §7.2.2).</summary>
public sealed class FileTreeViewTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-tree");

    public void Dispose() => _notebook.Delete(recursive: true);

    private void Touch(string relative)
    {
        var path = Path.Combine(_notebook.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private (FileTreeView Tree, Window Window) Open()
    {
        // Notebook layout: root/{ sub/child.md, apple.md, banana.md }
        Touch("sub/child.md");
        Touch("apple.md");
        Touch("banana.md");

        var tree = new FileTreeView();
        var window = new Window { Content = tree };
        window.Show();
        tree.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        tree.FocusTree();
        return (tree, window);
    }

    [AvaloniaFact]
    public void Root_is_expanded_and_folders_sort_before_files()
    {
        var (tree, _) = Open();

        // Root shown and expanded; its children are folder-first (sub) then files alphabetically.
        Assert.Equal([_notebook.Name, "sub", "apple.md", "banana.md"], tree.VisibleNamesForTest);
        Assert.Equal(_notebook.Name, tree.SelectedNameForTest);
    }

    [AvaloniaFact]
    public void Right_expands_then_descends_and_Left_collapses_then_ascends()
    {
        var (tree, window) = Open();

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // → sub
        Assert.Equal("sub", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); // expand sub in place
        Assert.Contains("child.md", tree.VisibleNamesForTest);
        Assert.Equal("sub", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); // descend to child.md
        Assert.Equal("child.md", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None); // file → ascend to parent sub
        Assert.Equal("sub", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None); // open folder → collapse
        Assert.DoesNotContain("child.md", tree.VisibleNamesForTest);
        Assert.Equal("sub", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None); // collapsed folder → ascend to root
        Assert.Equal(_notebook.Name, tree.SelectedNameForTest);
    }

    [AvaloniaFact]
    public void Enter_on_a_file_activates_it_and_on_a_folder_toggles()
    {
        var (tree, window) = Open();
        string? activated = null;
        tree.FileActivated += node => activated = node.Name;

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // sub
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);     // folder → expands
        Assert.Contains("child.md", tree.VisibleNamesForTest);
        Assert.Null(activated);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // child.md
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);     // file → activate
        Assert.Equal("child.md", activated);
    }

    [AvaloniaFact]
    public void Type_to_search_jumps_to_a_matching_name()
    {
        var (tree, window) = Open();

        window.KeyTextInput("b"); // from the root, jump to the next name matching "b" → banana.md
        Assert.Equal("banana.md", tree.SelectedNameForTest);
    }

    [AvaloniaFact]
    public void F2_and_Delete_raise_mutation_requests_for_the_selected_node()
    {
        var (tree, window) = Open();
        string? renamed = null;
        string? deleted = null;
        tree.RenameRequested += node => renamed = node.Name;
        tree.DeleteRequested += node => deleted = node.Name;

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // sub
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // apple.md
        Assert.Equal("apple.md", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        Assert.Equal("apple.md", renamed);

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Assert.Equal("apple.md", deleted);
    }

    [AvaloniaFact]
    public void The_notebook_root_cannot_be_renamed_or_deleted()
    {
        var (tree, window) = Open(); // FocusTree selects the root
        var fired = false;
        tree.RenameRequested += _ => fired = true;
        tree.DeleteRequested += _ => fired = true;

        Assert.Equal(_notebook.Name, tree.SelectedNameForTest);
        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.False(fired);
    }
}
