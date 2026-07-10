using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Pinned-folder chips and the scoped file tree (PRD §7.2 — the prototype behaviour).</summary>
public sealed class FilesPanelTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-files");

    public void Dispose() => _notebook.Delete(recursive: true);

    private string Path(string relative) => System.IO.Path.Combine(_notebook.FullName, relative);

    private void Touch(string relative)
    {
        var path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private FilesPanel Open()
    {
        // root/{ Projects/{ Roadmap.md, Neural Nets/Attention.md }, Inbox.md }
        Touch("Projects/Roadmap.md");
        Touch("Projects/Neural Nets/Attention.md");
        Touch("Inbox.md");

        var panel = new FilesPanel();
        var window = new Window { Content = panel.View };
        window.Show();
        panel.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        return panel;
    }

    [AvaloniaFact]
    public void A_fresh_panel_shows_only_All_notes_and_no_scope()
    {
        var panel = Open();

        Assert.Equal(["All notes"], panel.ChipLabelsForTest);
        Assert.Null(panel.ScopePathForTest);
        Assert.False(panel.BreadcrumbVisibleForTest);
        // The notebook's contents are shown, but the root itself is not a row (the sidebar header names it).
        Assert.DoesNotContain(_notebook.Name, panel.TreeVisibleNamesForTest);
        Assert.Contains("Inbox.md", panel.TreeVisibleNamesForTest);
    }

    [AvaloniaFact]
    public void Pinning_a_folder_adds_a_chip_and_notifies()
    {
        var panel = Open();
        var changed = 0;
        panel.PinnedChanged += () => changed++;

        panel.PinForTest(Path("Projects"));

        Assert.Equal(["All notes", "Projects"], panel.ChipLabelsForTest);
        Assert.Equal([Path("Projects")], panel.PinnedPaths);
        Assert.Equal(1, changed);

        panel.PinForTest(Path("Projects")); // already pinned → no chip, no notification
        Assert.Equal(["All notes", "Projects"], panel.ChipLabelsForTest);
        Assert.Equal(1, changed);
    }

    [AvaloniaFact]
    public void Scoping_to_a_chip_re_roots_the_tree_and_shows_the_breadcrumb()
    {
        var panel = Open();
        panel.PinForTest(Path("Projects/Neural Nets"));

        panel.ScopeToForTest(Path("Projects/Neural Nets"));

        Assert.Equal(Path("Projects/Neural Nets"), panel.ScopePathForTest);
        Assert.True(panel.BreadcrumbVisibleForTest);
        // The scoped folder is named by the breadcrumb, not shown as a row — only its contents appear.
        Assert.Equal(["Attention.md"], panel.TreeVisibleNamesForTest);

        panel.ScopeToForTest(null); // "All notes" returns to the whole notebook
        Assert.Null(panel.ScopePathForTest);
        Assert.False(panel.BreadcrumbVisibleForTest);
        Assert.DoesNotContain(_notebook.Name, panel.TreeVisibleNamesForTest);
        Assert.Contains("Inbox.md", panel.TreeVisibleNamesForTest);
    }

    [AvaloniaFact]
    public void Unpinning_the_scoped_folder_falls_back_to_All_notes()
    {
        var panel = Open();
        panel.PinForTest(Path("Projects"));
        panel.ScopeToForTest(Path("Projects"));
        Assert.Equal(Path("Projects"), panel.ScopePathForTest);

        panel.UnpinForTest(Path("Projects"));

        Assert.Equal(["All notes"], panel.ChipLabelsForTest);
        Assert.Null(panel.ScopePathForTest); // scoped into the unpinned folder → back to All notes
        Assert.False(panel.BreadcrumbVisibleForTest);
    }

    [AvaloniaFact]
    public void Reordering_chips_changes_their_order_and_notifies()
    {
        var panel = Open();
        panel.PinForTest(Path("Projects"));
        panel.PinForTest(Path("Inbox.md")); // (any folder path works for the ordering test)
        var changed = 0;
        panel.PinnedChanged += () => changed++;

        panel.ReorderForTest(Path("Inbox.md"), Path("Projects"), before: true);

        Assert.Equal([Path("Inbox.md"), Path("Projects")], panel.PinnedPaths);
        Assert.Equal(["All notes", "Inbox.md", "Projects"], panel.ChipLabelsForTest);
        Assert.Equal(1, changed);
    }

    [AvaloniaFact]
    public void SetPinned_drops_folders_that_no_longer_exist()
    {
        var panel = Open();

        panel.SetPinned([Path("Projects"), Path("Ghost")]);

        Assert.Equal([Path("Projects")], panel.PinnedPaths);
        Assert.Equal(["All notes", "Projects"], panel.ChipLabelsForTest);
    }
}
