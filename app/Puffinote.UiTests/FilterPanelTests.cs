using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The sidebar's Filter panel (PRD §7.2.3): the file tree, narrowed to the entries whose names contain what
/// you type. What is worth pinning is that it is the <em>tree</em> that narrows — the same rows, reached from
/// the same switcher row — that an empty box hides nothing, and that the folders above a match open
/// themselves so the match is a row you can see rather than one you have to go and find.
/// </summary>
public sealed class FilterPanelTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-filterpanel");

    public void Dispose() => _notebook.Delete(recursive: true);

    private string Path(string relative) => System.IO.Path.Combine(_notebook.FullName, relative);

    private void Touch(string relative)
    {
        var path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    /// <summary>root/{ Projects/{ Roadmap.md, Budget.ods, Neural Nets/Attention.md }, Inbox.md, notes.txt }</summary>
    private FilterPanel Open()
    {
        Touch("Projects/Roadmap.md");
        Touch("Projects/Budget.ods");
        Touch("Projects/Neural Nets/Attention.md");
        Touch("Inbox.md");
        Touch("notes.txt");

        var panel = new FilterPanel();
        var window = new Window { Content = panel.View };
        window.Show();
        panel.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        Dispatcher.UIThread.RunJobs();
        return panel;
    }

    private static async Task<FilterPanel> TypeAsync(FilterPanel panel, string query)
    {
        panel.QueryForTest = query;
        await panel.ApplyFilterNowForTest(); // the debounce, skipped: a test shouldn't wait out 150ms
        Dispatcher.UIThread.RunJobs();
        return panel;
    }

    [AvaloniaFact]
    public void An_empty_box_shows_the_whole_notebook()
    {
        var panel = Open();

        Assert.Equal(["Projects", "Inbox.md", "notes.txt"], panel.VisibleNamesForTest);
        Assert.Equal(string.Empty, panel.StatusForTest); // nothing to count, so no line at all
    }

    [AvaloniaFact]
    public async Task Typing_leaves_the_matches_and_the_folders_that_lead_to_them()
    {
        var panel = await TypeAsync(Open(), "attention");

        // Both folders are shown *and opened*, so the match is a visible row rather than one behind two
        // chevrons — and nothing else in the notebook survives.
        Assert.Equal(["Projects", "Neural Nets", "Attention.md"], panel.VisibleNamesForTest);
        Assert.Equal("1 match", panel.StatusForTest);
    }

    [AvaloniaFact]
    public async Task An_extension_is_part_of_what_you_can_type()
    {
        var panel = await TypeAsync(Open(), "md");

        Assert.Equal(["Projects", "Neural Nets", "Attention.md", "Roadmap.md", "Inbox.md"], panel.VisibleNamesForTest);
        Assert.Equal("3 matches", panel.StatusForTest);
    }

    [AvaloniaFact]
    public async Task A_query_that_matches_nothing_says_so()
    {
        // An empty tree with nothing above it reads as a notebook that has lost its files; the line is what
        // says otherwise.
        var panel = await TypeAsync(Open(), "zzz");

        Assert.Empty(panel.VisibleNamesForTest);
        Assert.Equal("No matches", panel.StatusForTest);
    }

    [AvaloniaFact]
    public async Task Clearing_the_box_gives_the_notebook_back_around_what_you_found()
    {
        var panel = await TypeAsync(Open(), "attention");
        await TypeAsync(panel, string.Empty);

        // Everything is a row again — nothing stays hidden — and the folders the filter opened on the way to
        // the match are still open. You went looking for a file; clearing the box hands you back the notebook
        // with that file still on screen, in its place, rather than shutting the tree over it.
        Assert.Equal(
            ["Projects", "Neural Nets", "Attention.md", "Budget.ods", "Roadmap.md", "Inbox.md", "notes.txt"],
            panel.VisibleNamesForTest);
        Assert.Equal(string.Empty, panel.StatusForTest);
    }

    [AvaloniaFact]
    public async Task Opening_another_notebook_empties_the_box()
    {
        // The filter was typed about the notebook you had open. Carried into the next one it would open on a
        // tree with most of itself missing, for reasons off screen.
        var panel = await TypeAsync(Open(), "attention");

        var second = Directory.CreateTempSubdirectory("puffinote-filterpanel-2");
        try
        {
            File.WriteAllText(System.IO.Path.Combine(second.FullName, "Elsewhere.md"), "");
            panel.SetRoot(new NotebookNode(second.FullName, isDirectory: true));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Empty, panel.QueryForTest);
            Assert.Equal(["Elsewhere.md"], panel.VisibleNamesForTest);
        }
        finally
        {
            second.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_file_created_after_the_filter_was_typed_is_judged_by_it_too()
    {
        var panel = await TypeAsync(Open(), "md");
        Touch("Later.md");
        Touch("Later.log");

        panel.Refresh();                        // what the shell calls after a mutation
        await panel.ApplyFilterNowForTest();    // …which re-runs the filter (debounced in the app)
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Later.md", panel.VisibleNamesForTest);
        Assert.DoesNotContain("Later.log", panel.VisibleNamesForTest);
    }

    [AvaloniaFact]
    public void The_sidebar_offers_it_from_the_switcher_row_next_to_Files()
    {
        var sidebar = new SidebarView();
        var window = new Window { Content = sidebar };
        window.Show();

        var button = sidebar.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Tag == "filter");
        Assert.Equal("Filter", AutomationProperties.GetName(button));

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("filter", sidebar.ActivePanelId);
    }

    [AvaloniaFact]
    public async Task What_the_shell_does_to_the_tree_reaches_this_one_too()
    {
        // The sidebar has two trees now, and everything the shell says about "the tree" — here is the
        // notebook, re-read it, this document is the open one — has to reach both, or the Filter panel would
        // be a tree showing the notebook you had open before.
        Touch("Inbox.md");
        var sidebar = new SidebarView();
        var window = new Window { Content = sidebar };
        window.Show();
        sidebar.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        sidebar.Activate("filter");
        Dispatcher.UIThread.RunJobs();

        var panel = sidebar.FilterPanelForTest;
        Assert.Contains("Inbox.md", panel.VisibleNamesForTest);

        // And the selection the shell acts on is the one in the panel you are looking at.
        await TypeAsync(panel, "inbox");
        Assert.Equal(["Inbox.md"], panel.VisibleNamesForTest);
        Assert.Equal("Inbox.md", sidebar.SelectedTreeNode?.Name);
    }
}
