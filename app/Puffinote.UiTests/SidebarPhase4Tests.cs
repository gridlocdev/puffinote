using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 4A: the sidebar panel switcher, recent-files tracking, and quick-open (⌘O).</summary>
public sealed class SidebarPhase4Tests
{
    private static RawInputModifiers Primary =>
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaFact]
    public void Switcher_activates_panels_and_focus_tree_returns_to_files()
    {
        var sidebar = new SidebarView();
        var window = new Window { Content = sidebar };
        window.Show();

        Assert.Equal("files", sidebar.ActivePanelId); // the tree is the default panel

        sidebar.Activate("recent");
        Assert.Equal("recent", sidebar.ActivePanelId);

        sidebar.Activate("does-not-exist"); // unknown id is a no-op
        Assert.Equal("recent", sidebar.ActivePanelId);

        sidebar.FocusTree(); // the "focus file tree" command also switches back to the tree
        Assert.Equal("files", sidebar.ActivePanelId);
    }

    [AvaloniaFact]
    public async Task Opening_files_records_them_most_recent_first()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-recent");
        try
        {
            var a = Path.Combine(dir.FullName, "a.md");
            var b = Path.Combine(dir.FullName, "b.md");
            File.WriteAllText(a, "# a");
            File.WriteAllText(b, "# b");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            await window.OpenFileAsync(a);
            await window.OpenFileAsync(b);

            // Most-recent first; re-opening a promotes it back to the front without duplicating it.
            Assert.Equal([b, a], window.RecentForTest);

            await window.OpenFileAsync(a);
            Assert.Equal([a, b], window.RecentForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Unsupported_and_missing_files_are_not_recorded()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-recent-skip");
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            var image = Path.Combine(dir.FullName, "photo.png");
            File.WriteAllText(note, "# note");
            File.WriteAllText(image, "not really a png");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            await window.OpenFileAsync(note);
            await window.OpenFileAsync(image);                               // no pane → not recorded
            await window.OpenFileAsync(Path.Combine(dir.FullName, "gone.md")); // missing → not recorded

            Assert.Equal([note], window.RecentForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Quick_open_shortcut_opens_the_overlay_only_when_a_notebook_is_open()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-quickopen");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "note.md"), "# note");

            var window = new MainWindow(new NotebookService());
            window.Show();

            window.KeyPressQwerty(PhysicalKey.O, Primary); // no notebook yet → command disabled, nothing opens
            Assert.False(window.IsOverlayOpenForTest);

            window.OpenNotebook(dir.FullName);
            window.KeyPressQwerty(PhysicalKey.O, Primary); // reserved ⌘O opens quick-open over the notebook
            Assert.True(window.IsOverlayOpenForTest);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); // Esc dismisses the palette
            Assert.False(window.IsOverlayOpenForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Deleting_a_file_from_the_tree_confirms_then_removes_it()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-delete");
        MainWindow? window = null;
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(note, "# note");

            window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            // Select the file in the tree and press Delete → the shell opens a confirm dialog.
            window.SidebarForTest.RevealInTree(note);
            window.SidebarForTest.FocusTree();
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            Assert.True(window.IsOverlayOpenForTest);
            Assert.True(File.Exists(note)); // not gone until confirmed

            // Confirm it: click the dialog's "Delete" button.
            ClickButton(window, "Delete");
            await Task.Yield();

            Assert.False(window.IsOverlayOpenForTest);
            Assert.False(File.Exists(note));
        }
        finally
        {
            // The mutation kicked a background index rebuild, which ends by writing the notebook's .puffinote/
            // cache. Deleting the notebook without waiting for that write races it — the delete walks a directory
            // the rebuild is still filling, and fails with "Directory not empty".
            await (window?.IndexRebuildForTest ?? Task.CompletedTask);
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Deleting_a_file_thats_open_in_a_tab_closes_the_tab()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-delete-open");
        MainWindow? window = null;
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(note, "# note");

            window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(note);
            Assert.Contains("note.md", window.Title); // the note is the active tab

            window.SidebarForTest.RevealInTree(note);
            window.SidebarForTest.FocusTree();
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            ClickButton(window, "Delete"); // confirm
            await Task.Yield();

            Assert.False(File.Exists(note));
            Assert.DoesNotContain("note.md", window.Title); // its tab closed with it
        }
        finally
        {
            await (window?.IndexRebuildForTest ?? Task.CompletedTask); // let the .puffinote/ write land first
            dir.Delete(recursive: true);
        }
    }

    private static void ClickButton(Visual root, string content)
    {
        var button = root.GetVisualDescendants()
            .OfType<Button>()
            .First(b => (b.Content as string) == content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
