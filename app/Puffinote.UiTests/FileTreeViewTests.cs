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
    public void The_root_is_hidden_and_its_children_show_at_the_top_level_folder_first()
    {
        var (tree, _) = Open();

        // The notebook root is not a row of its own (the sidebar header names it); the tree shows only what's
        // inside it — folder-first (sub) then files alphabetically — and focus lands on the first entry.
        Assert.Equal(["sub", "apple.md", "banana.md"], tree.VisibleNamesForTest);
        Assert.Equal("sub", tree.SelectedNameForTest);
    }

    [AvaloniaFact]
    public void Right_expands_then_descends_and_Left_collapses_then_ascends()
    {
        var (tree, window) = Open();
        Assert.Equal("sub", tree.SelectedNameForTest); // first top-level entry, selected on focus

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

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None); // top-level collapsed folder → nothing above to ascend to
        Assert.Equal("sub", tree.SelectedNameForTest);
    }

    [AvaloniaFact]
    public void Enter_on_a_file_activates_it_and_on_a_folder_toggles()
    {
        var (tree, window) = Open();
        string? activated = null;
        tree.FileActivated += node => activated = node.Name;

        Assert.Equal("sub", tree.SelectedNameForTest);                       // first entry
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

        window.KeyTextInput("b"); // from the first entry, jump to the next name matching "b" → banana.md
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

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // sub → apple.md
        Assert.Equal("apple.md", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        Assert.Equal("apple.md", renamed);

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Assert.Equal("apple.md", deleted);
    }

    [AvaloniaFact]
    public void Cmd_N_and_Cmd_Shift_N_request_a_new_note_and_a_new_folder_at_the_selection()
    {
        var (tree, window) = Open();
        NotebookNode? noteContext = null;
        NotebookNode? folderContext = null;
        var noteFired = 0;
        tree.NewNoteRequested += node =>
        {
            noteContext = node;
            noteFired++;
        };
        tree.NewFolderRequested += node => folderContext = node;

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // sub → apple.md
        Assert.Equal("apple.md", tree.SelectedNameForTest);

        // ⌘N (Ctrl+N off-mac) → new note, passing the current selection as the placement context.
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control);
        Assert.Equal(1, noteFired);
        Assert.Equal("apple.md", noteContext?.Name);

        // ⌘⇧N → new folder.
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("apple.md", folderContext?.Name);

        Assert.Equal(1, noteFired); // the folder chord didn't also fire the note request
    }

    [AvaloniaFact]
    public void The_notebook_root_is_not_shown_as_a_row_so_it_can_never_be_targeted()
    {
        var (tree, _) = Open();

        // With the root hidden entirely, it isn't a visible row and can't be selected — so it can never be
        // the target of a rename/delete. Focus lands on a child instead of the notebook root.
        Assert.DoesNotContain(_notebook.Name, tree.VisibleNamesForTest);
        Assert.NotEqual(_notebook.Name, tree.SelectedNameForTest);
    }
}
