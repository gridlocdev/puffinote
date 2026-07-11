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

/// <summary>
/// The sidebar's mouse map. A single click opens — the reason to click a note is almost always to read it, so
/// charging two clicks for it taxes every navigation — and Shift+click selects without opening, which is how
/// you target a file to rename, pin, or delete without disturbing what you're reading.
/// </summary>
public sealed class FileTreeClickTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-click");

    public void Dispose() => _notebook.Delete(recursive: true);

    private (FileTreeView Tree, Window Window) Open()
    {
        // root/{ sub/child.md, apple.md } — a folder first, then a file.
        Directory.CreateDirectory(Path.Combine(_notebook.FullName, "sub"));
        File.WriteAllText(Path.Combine(_notebook.FullName, "sub", "child.md"), "");
        File.WriteAllText(Path.Combine(_notebook.FullName, "apple.md"), "");

        var tree = new FileTreeView();
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

    /// <summary>The row whose label is <paramref name="name"/>.</summary>
    private static ListBoxItem Row(Window window, string name) =>
        window.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Single(i => i.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == name));

    /// <summary>Click the row whose label is <paramref name="name"/>, in window coordinates.</summary>
    private static void Click(Window window, string name, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var row = Row(window, name);
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left, modifiers);
        window.MouseUp(centre, MouseButton.Left, modifiers);
        Settle(window);
    }

    [AvaloniaFact]
    public void A_single_click_on_a_file_opens_it()
    {
        var (tree, window) = Open();
        string? activated = null;
        tree.FileActivated += node => activated = node.Name;

        Click(window, "apple.md");

        Assert.Equal("apple.md", activated);          // one click, not two
        Assert.Equal("apple.md", tree.SelectedNameForTest); // and it selected the row it opened
    }

    [AvaloniaFact]
    public void A_single_click_on_a_folder_toggles_it()
    {
        var (tree, window) = Open();

        Assert.DoesNotContain("child.md", tree.VisibleNamesForTest);

        Click(window, "sub");
        Assert.Contains("child.md", tree.VisibleNamesForTest); // expanded

        // Click elsewhere first: two clicks on the same row in quick succession are a *double*-click, which is
        // deliberately inert (see below). This is a fresh gesture on the folder.
        Click(window, "apple.md");
        Click(window, "sub");
        Assert.DoesNotContain("child.md", tree.VisibleNamesForTest); // and collapses again
    }

    [AvaloniaFact]
    public void A_double_click_still_activates_only_once()
    {
        // Plenty of users will double-click out of habit. The second click of a double-click raises DoubleTapped
        // rather than Tapped, and nothing listens for it — so a double-click degrades to a single activation
        // instead of opening a file twice, or expanding a folder and immediately collapsing it again.
        var (tree, window) = Open();
        var opens = 0;
        tree.FileActivated += _ => opens++;

        var row = Row(window, "apple.md");
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        window.MouseDown(centre, MouseButton.Left); // the second click of the pair
        window.MouseUp(centre, MouseButton.Left);
        Settle(window);

        Assert.Equal(1, opens);
    }

    [AvaloniaFact]
    public void Shift_click_selects_without_opening()
    {
        var (tree, window) = Open();
        string? activated = null;
        tree.FileActivated += node => activated = node.Name;

        Click(window, "apple.md", RawInputModifiers.Shift);

        Assert.Equal("apple.md", tree.SelectedNameForTest); // selected...
        Assert.Null(activated);                            // ...but not opened
    }

    [AvaloniaFact]
    public void Shift_click_on_a_folder_selects_it_without_toggling()
    {
        var (tree, window) = Open();

        Click(window, "sub", RawInputModifiers.Shift);

        Assert.Equal("sub", tree.SelectedNameForTest);
        Assert.DoesNotContain("child.md", tree.VisibleNamesForTest); // still collapsed
    }
}
