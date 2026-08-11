using Avalonia;
using Avalonia.Automation;
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

/// <summary>
/// Dragging a row onto a folder moves it there. The gesture is the tree's own — it draws the ghost, the drop
/// indicator, and opens a folder the drag rests on — so these exercise the real pointer path: press a row,
/// move onto another, release.
/// </summary>
public sealed class FileTreeDragTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-drag");

    public void Dispose() => _notebook.Delete(recursive: true);

    private string Path(params string[] parts) => System.IO.Path.Combine([_notebook.FullName, .. parts]);

    /// <summary>root/{ sub/, deep/inner/, apple.md } — a folder to drop into, a nested one, and a file to drag.</summary>
    private (FileTreeView Tree, Window Window) Open()
    {
        Directory.CreateDirectory(Path("sub"));
        File.WriteAllText(Path("sub", "kept.md"), "");
        Directory.CreateDirectory(Path("deep", "inner"));
        File.WriteAllText(Path("apple.md"), "note");

        var tree = new FileTreeView { UseMacKeymap = false };
        var window = new Window { Width = 300, Height = 400, Content = tree };
        window.Show();
        tree.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        Settle(window);
        return (tree, window);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static ListBoxItem Row(Window window, string name) =>
        window.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Single(i => i.GetVisualDescendants().OfType<Control>().Any(c => AutomationProperties.GetName(c) == name));

    private static Point Centre(Window window, string name)
    {
        var row = Row(window, name);
        return row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
    }

    /// <summary>Press <paramref name="source"/> and drag the pointer onto <paramref name="target"/>, still held.</summary>
    private static void DragOnto(Window window, string source, string target)
    {
        window.MouseDown(Centre(window, source), MouseButton.Left);
        window.MouseMove(Centre(window, target), RawInputModifiers.LeftMouseButton);
        Settle(window);
    }

    [AvaloniaFact]
    public void Dragging_a_file_onto_a_folder_moves_it_there()
    {
        var (tree, window) = Open();
        (NotebookNode Node, string Directory)? move = null;
        tree.MoveRequested += (node, directory) => move = (node, directory);

        DragOnto(window, "apple.md", "sub");
        Assert.True(tree.IsDraggingForTest);
        Assert.Equal("apple.md", tree.GhostNameForTest); // the ghost says what is being moved
        Assert.Equal(Path("sub"), tree.DropTarget);      // and the indicator, where it would land

        window.MouseUp(Centre(window, "sub"), MouseButton.Left);
        Settle(window);

        Assert.NotNull(move);
        Assert.Equal("apple.md", move!.Value.Node.Name);
        Assert.Equal(Path("sub"), move.Value.Directory);
        Assert.False(tree.IsDraggingForTest); // and the drag is over
    }

    [AvaloniaFact]
    public void Dropping_on_a_file_moves_into_the_folder_that_holds_it()
    {
        // "Onto a file" plainly means "in there, beside it" — the file itself can't hold anything.
        var (tree, window) = Open();
        tree.SelectPath(Path("sub"));
        Settle(window);
        Row(window, "sub"); // the folder is open, so kept.md is a row
        tree.BeginDragForTest(new NotebookNode(Path("apple.md"), isDirectory: false));
        tree.DragOverForTest(new NotebookNode(Path("sub", "kept.md"), isDirectory: false));

        Assert.Equal(Path("sub"), tree.DropTarget);
    }

    [AvaloniaFact]
    public void A_move_that_would_change_nothing_or_swallow_itself_is_refused()
    {
        var (tree, _) = Open();
        var folder = new NotebookNode(Path("deep"), isDirectory: true);
        tree.BeginDragForTest(folder);

        // Into itself, and into its own descendant: both would take the folder with it.
        tree.DragOverForTest(folder);
        Assert.Null(tree.DropTarget);
        tree.DragOverForTest(new NotebookNode(Path("deep", "inner"), isDirectory: true));
        Assert.Null(tree.DropTarget);

        // Into the folder it already lives in: nothing would happen, so nothing is offered.
        tree.DragOverForTest(hovered: null); // the empty space below the rows — the notebook's top level
        Assert.Null(tree.DropTarget);

        // A folder it isn't in is a real move.
        tree.DragOverForTest(new NotebookNode(Path("sub"), isDirectory: true));
        Assert.Equal(Path("sub"), tree.DropTarget);
    }

    [AvaloniaFact]
    public void Dropping_on_the_empty_space_moves_to_the_top_level()
    {
        var (tree, _) = Open();
        string? directory = null;
        tree.MoveRequested += (_, d) => directory = d;

        tree.BeginDragForTest(new NotebookNode(Path("sub", "kept.md"), isDirectory: false));
        tree.DragOverForTest(hovered: null);
        tree.DropForTest();

        Assert.Equal(_notebook.FullName, directory);
    }

    [AvaloniaFact]
    public void Resting_on_a_collapsed_folder_opens_it_so_you_can_aim_inside()
    {
        var (tree, _) = Open();
        Assert.DoesNotContain("kept.md", tree.VisibleNamesForTest); // "sub" starts shut

        tree.BeginDragForTest(new NotebookNode(Path("apple.md"), isDirectory: false));
        tree.DragOverForTest(new NotebookNode(Path("sub"), isDirectory: true));
        tree.ElapseHoverExpandForTest(); // the dwell, without a second of wall clock

        Assert.Contains("kept.md", tree.VisibleNamesForTest);
    }

    [AvaloniaFact]
    public void Escape_abandons_the_drag_without_moving_anything()
    {
        var (tree, window) = Open();
        var moves = 0;
        tree.MoveRequested += (_, _) => moves++;

        DragOnto(window, "apple.md", "sub");
        Assert.True(tree.IsDraggingForTest);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Settle(window);

        Assert.False(tree.IsDraggingForTest);
        Assert.Null(tree.GhostNameForTest);

        window.MouseUp(Centre(window, "sub"), MouseButton.Left);
        Settle(window);
        Assert.Equal(0, moves);
    }

    [AvaloniaFact]
    public void A_click_that_never_moved_still_opens_the_file()
    {
        // The press that may become a drag must not cost the tree its single-click-to-open.
        var (tree, window) = Open();
        string? opened = null;
        tree.FileActivated += node => opened = node.Name;

        var centre = Centre(window, "apple.md");
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Settle(window);

        Assert.Equal("apple.md", opened);
        Assert.False(tree.IsDraggingForTest);
    }
}
