using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        var window = new Window { Width = 300, Height = 400, Content = tree }; // sized, so the rows have hit-testable bounds
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
        tree.UseMacKeymap = false; // the Windows/Linux map, where Enter opens (the macOS one is its own test)
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
    public void On_macOS_Return_renames_and_Space_opens_the_selection()
    {
        var (tree, window) = Open();
        tree.UseMacKeymap = true; // exercise the macOS map on the Linux CI
        string? renamed = null;
        string? activated = null;
        tree.RenameRequested += node => renamed = node.Name;
        tree.FileActivated += node => activated = node.Name;

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // sub → apple.md
        Assert.Equal("apple.md", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // Finder: Return renames, doesn't open
        Assert.Equal("apple.md", renamed);
        Assert.Null(activated);

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); // Space opens the file
        Assert.Equal("apple.md", activated);
    }

    [AvaloniaFact]
    public void On_macOS_Cmd_chords_open_ascend_to_the_enclosing_folder_and_delete()
    {
        var (tree, window) = Open();
        tree.UseMacKeymap = true;
        string? activated = null;
        string? deleted = null;
        tree.FileActivated += node => activated = node.Name;
        tree.DeleteRequested += node => deleted = node.Name;

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); // expand sub
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); // descend to child.md
        Assert.Equal("child.md", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Meta); // ⌘↓ opens the file
        Assert.Equal("child.md", activated);

        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Meta); // ⌘↑ jumps to the enclosing folder
        Assert.Equal("sub", tree.SelectedNameForTest);

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None); // a bare Delete is inert on macOS
        Assert.Null(deleted);

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.Meta); // ⌘⌫ moves to trash
        Assert.Equal("sub", deleted);
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
        tree.UseMacKeymap = false; // bare Delete deletes only off macOS; ⌘⌫ is covered by the macOS test
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
        window.KeyPressQwerty(PhysicalKey.N, TestKeys.RawCommand);
        Assert.Equal(1, noteFired);
        Assert.Equal("apple.md", noteContext?.Name);

        // ⌘⇧N → new folder.
        window.KeyPressQwerty(PhysicalKey.N, TestKeys.RawCommand | RawInputModifiers.Shift);
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

    // ---- Context menus (folder vs. file vs. the empty space) -------------------------------------

    [AvaloniaFact]
    public void A_folders_menu_creates_inside_it_pins_it_and_toggles_it()
    {
        var (tree, _) = Open();
        Assert.Equal("sub", tree.SelectedNameForTest); // the folder, selected on focus

        Assert.Equal(
            [
                "Expand", "—",                                     // "sub" holds child.md, so it can be opened
                "New Note…", "New Spreadsheet…", "New Folder…", "—", // a folder is the only row that creates
                "Pin to Sidebar", SystemFileManager.RevealLabel, "—",
                "Rename…", "Delete",
            ],
            tree.ContextMenuHeadersForTest);

        // The creation commands target the folder itself (the shell puts the new entry inside it).
        NotebookNode? noteContext = null;
        tree.NewNoteRequested += node => noteContext = node;
        tree.InvokeContextMenuItemForTest("New Note…");
        Assert.Equal("sub", noteContext?.Name);

        // Expand/Collapse names what the row will do next, and does it.
        tree.InvokeContextMenuItemForTest("Expand");
        Assert.Contains("child.md", tree.VisibleNamesForTest);
        Assert.Contains("Collapse", tree.ContextMenuHeadersForTest);
        Assert.DoesNotContain("Expand", tree.ContextMenuHeadersForTest);
    }

    [AvaloniaFact]
    public void An_empty_folder_is_offered_no_expand_and_a_pinned_one_is_offered_unpin()
    {
        var (tree, _) = Open();
        Directory.CreateDirectory(Path.Combine(_notebook.FullName, "empty"));
        tree.Refresh();
        tree.SetPinnedPaths(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(_notebook.FullName, "empty") });
        tree.SelectPath(Path.Combine(_notebook.FullName, "empty"));
        Assert.Equal("empty", tree.SelectedNameForTest);

        var headers = tree.ContextMenuHeadersForTest;
        Assert.DoesNotContain("Expand", headers);           // nothing inside it to open — as the row's missing chevron says
        Assert.DoesNotContain("Collapse", headers);
        Assert.Contains("Unpin from Sidebar", headers);     // already pinned → the command is the opposite one
        Assert.DoesNotContain("Pin to Sidebar", headers);
    }

    [AvaloniaFact]
    public void A_files_menu_opens_it_and_never_offers_to_create_anything()
    {
        var (tree, window) = Open();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // sub → apple.md
        Assert.Equal("apple.md", tree.SelectedNameForTest);

        Assert.Equal(
            ["Open", SystemFileManager.RevealLabel, "—", "Rename…", "Delete"],
            tree.ContextMenuHeadersForTest);

        string? activated = null;
        tree.FileActivated += node => activated = node.Name;
        tree.InvokeContextMenuItemForTest("Open");
        Assert.Equal("apple.md", activated);
    }

    [AvaloniaFact]
    public void A_file_no_editor_here_can_open_is_handed_to_the_default_app_instead()
    {
        var (tree, _) = Open();
        Touch("diagram.png"); // no Puffinote pane opens a .png (PRD §7.2)
        tree.Refresh();
        tree.SelectPath(Path.Combine(_notebook.FullName, "diagram.png"));

        NotebookNode? external = null;
        tree.OpenExternallyRequested += node => external = node;

        Assert.Contains("Open in Default App", tree.ContextMenuHeadersForTest);
        Assert.DoesNotContain("Open", tree.ContextMenuHeadersForTest); // it would open a tab that can't render it

        tree.InvokeContextMenuItemForTest("Open in Default App");
        Assert.Equal("diagram.png", external?.Name);
    }

    [AvaloniaFact]
    public void A_right_click_targets_the_row_under_the_pointer_and_opens_that_rows_own_menu()
    {
        // The tests above read the menu through a seam. This one right-clicks for real, because the items are
        // built when the menu opens: if that hook stopped firing, every seam test would still pass while the
        // shipped menu came up empty. It also pins the two halves together — the click selects the row, and the
        // menu is built for the row the click selected.
        var (tree, window) = Open();
        var list = tree.GetVisualDescendants().OfType<ListBox>().First();
        var menu = list.ContextMenu!;

        RightClick(window, RowCenter(list, window, index: 1)); // apple.md — the row below the "sub" folder
        Assert.Equal("apple.md", tree.SelectedNameForTest);
        Assert.True(menu.IsOpen);
        Assert.Equal(
            ["Open", SystemFileManager.RevealLabel, "Rename…", "Delete"],
            menu.Items.OfType<MenuItem>().Select(item => item.Header as string));
        menu.Close();

        // Below the last row: nothing was clicked, so the selection is dropped and the scope's menu comes up —
        // no Rename/Delete aimed at a row the user isn't pointing at.
        var belowTheRows = RowCenter(list, window, index: 2) + new Vector(0, 100);
        RightClick(window, belowTheRows);
        Assert.Null(tree.SelectedNameForTest);
        Assert.Equal(
            ["New Note…", "New Spreadsheet…", "New Folder…", "Collapse All"],
            menu.Items.OfType<MenuItem>().Select(item => item.Header as string));
        menu.Close();
    }

    /// <summary>The middle of tree row <paramref name="index"/>, in window coordinates — read off the laid-out row.</summary>
    private static Point RowCenter(ListBox list, Window window, int index)
    {
        Dispatcher.UIThread.RunJobs(); // the rows are only realized once a layout pass has run
        var row = list.ContainerFromIndex(index)!;
        return row.TranslatePoint(row.Bounds.Center - row.Bounds.Position, window)!.Value;
    }

    private static void RightClick(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
    }

    [AvaloniaFact]
    public void The_empty_space_below_the_rows_gets_the_scopes_own_menu()
    {
        var (tree, _) = Open();
        tree.ClearSelectionForTest(); // what a right click on the empty space does

        Assert.Equal(
            ["New Note…", "New Spreadsheet…", "New Folder…", "—", "Collapse All"],
            tree.ContextMenuHeadersForTest);

        // Nothing was clicked, so nothing can be renamed or deleted by accident.
        Assert.DoesNotContain("Rename…", tree.ContextMenuHeadersForTest);
        Assert.DoesNotContain("Delete", tree.ContextMenuHeadersForTest);

        // A creation there lands at the top level of what the tree is showing.
        NotebookNode? context = null;
        tree.NewFolderRequested += node => context = node;
        tree.InvokeContextMenuItemForTest("New Folder…");
        Assert.Equal(_notebook.FullName, context?.Path);
    }
}
