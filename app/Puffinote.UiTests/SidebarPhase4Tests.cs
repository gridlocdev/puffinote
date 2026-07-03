using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
            Assert.Equal(new[] { b, a }, window.RecentForTest);

            await window.OpenFileAsync(a);
            Assert.Equal(new[] { a, b }, window.RecentForTest);
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

            Assert.Equal(new[] { note }, window.RecentForTest);
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
}
