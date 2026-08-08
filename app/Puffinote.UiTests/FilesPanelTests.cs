using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Puffinote.Components.Controls.Icons;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Entering a folder, the "In Folder" bar, and the scoped file tree (PRD §7.2).</summary>
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
    public void A_fresh_panel_shows_the_whole_notebook_and_no_folder_bar()
    {
        var panel = Open();

        Assert.Null(panel.ScopePathForTest);
        Assert.False(panel.FolderBarVisibleForTest); // nothing to name, and nowhere to go back to
        // The notebook's contents are shown, but the root itself is not a row (the sidebar header names it).
        Assert.DoesNotContain(_notebook.Name, panel.TreeVisibleNamesForTest);
        Assert.Contains("Inbox.md", panel.TreeVisibleNamesForTest);
    }

    [AvaloniaFact]
    public void Entering_a_folder_re_roots_the_tree_and_names_it_in_the_bar()
    {
        var panel = Open();

        panel.EnterFolderForTest(Path("Projects/Neural Nets"));

        Assert.Equal(Path("Projects/Neural Nets"), panel.ScopePathForTest);
        Assert.True(panel.FolderBarVisibleForTest);
        Assert.Equal("In Folder: Neural Nets", panel.FolderLabelTextForTest);
        // The folder you entered is named by the bar, not shown as a row — only its contents appear.
        Assert.Equal(["Attention.md"], panel.TreeVisibleNamesForTest);
    }

    [AvaloniaFact]
    public void The_back_arrow_returns_to_the_whole_notebook()
    {
        var panel = Open();
        panel.EnterFolderForTest(Path("Projects"));
        Assert.Equal("Back to all notes", panel.BackTooltipForTest);

        panel.ClickBackForTest();

        Assert.Null(panel.ScopePathForTest);
        Assert.False(panel.FolderBarVisibleForTest);
        Assert.Contains("Inbox.md", panel.TreeVisibleNamesForTest); // the rest of the notebook is back
    }

    [AvaloniaFact]
    public void Back_out_of_a_nested_folder_lands_in_the_one_it_was_entered_from()
    {
        var panel = Open();
        panel.EnterFolderForTest(Path("Projects"));
        panel.EnterFolderForTest(Path("Projects/Neural Nets"));

        // The arrow says where it goes, because from here that is no longer the notebook.
        Assert.Equal("Back to Projects", panel.BackTooltipForTest);
        panel.ClickBackForTest();

        Assert.Equal(Path("Projects"), panel.ScopePathForTest);
        Assert.Equal("In Folder: Projects", panel.FolderLabelTextForTest);
        Assert.Contains("Roadmap.md", panel.TreeVisibleNamesForTest);

        panel.ClickBackForTest(); // ...and one more step is the whole notebook
        Assert.Null(panel.ScopePathForTest);
        Assert.False(panel.FolderBarVisibleForTest);
    }

    [AvaloniaFact]
    public void Entering_the_folder_you_are_already_in_does_not_stack_another_step()
    {
        var panel = Open();
        panel.EnterFolderForTest(Path("Projects"));

        panel.EnterFolderForTest(Path("Projects"));

        panel.ClickBackForTest();
        Assert.Null(panel.ScopePathForTest); // one back, not two
    }

    [AvaloniaFact]
    public void Selecting_a_path_outside_the_entered_folder_leaves_it()
    {
        var panel = Open();
        panel.EnterFolderForTest(Path("Projects/Neural Nets"));

        panel.SelectPath(Path("Inbox.md")); // e.g. Quick Open landing on a file elsewhere in the notebook

        Assert.Null(panel.ScopePathForTest);
        Assert.False(panel.FolderBarVisibleForTest);
        Assert.Contains("Inbox.md", panel.TreeVisibleNamesForTest);
    }

    [AvaloniaFact]
    public void A_refresh_leaves_a_folder_that_no_longer_exists()
    {
        var panel = Open();
        panel.EnterFolderForTest(Path("Projects"));
        panel.EnterFolderForTest(Path("Projects/Neural Nets"));

        Directory.Delete(Path("Projects/Neural Nets"), recursive: true);
        panel.Refresh();

        // The folder is gone, so the sidebar falls back to the nearest one on the way in that is still there.
        Assert.Equal(Path("Projects"), panel.ScopePathForTest);
        Assert.Equal("In Folder: Projects", panel.FolderLabelTextForTest);
        Assert.Contains("Roadmap.md", panel.TreeVisibleNamesForTest);
    }

    [AvaloniaFact]
    public void The_back_arrow_draws_a_real_glyph()
    {
        // The arrow is the only thing that says the button goes *back*, and a key with no vendored SVG behind it
        // fails silently — an empty 24px button that still clicks.
        var panel = Open();
        panel.EnterFolderForTest(Path("Projects"));

        var geometry = LucideIcons.Get(panel.BackIconKeyForTest);
        Assert.NotNull(geometry);
        Assert.True(geometry.Bounds.Width > 0);
    }

    [AvaloniaFact]
    public void Opening_a_notebook_never_lands_inside_a_folder()
    {
        var panel = Open();
        panel.EnterFolderForTest(Path("Projects"));

        panel.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));

        Assert.Null(panel.ScopePathForTest);
        Assert.False(panel.FolderBarVisibleForTest);
    }
}
