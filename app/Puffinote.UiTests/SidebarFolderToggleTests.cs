using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Puffinote.Components.Controls.Icons;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The sidebar header's one folder button and its two states. It used to say "Collapse all folders" forever —
/// including when every folder was already shut and pressing it could do nothing at all. It now says what it
/// will actually do next, which is the only way a user can tell a fully-collapsed tree from a flat one.
/// </summary>
public sealed class SidebarFolderToggleTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-folder-toggle");

    public void Dispose() => _notebook.Delete(recursive: true);

    private string Path(string relative) => System.IO.Path.Combine(_notebook.FullName, relative);

    private void Touch(string relative)
    {
        var path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    /// <summary>A sidebar showing the temp notebook, in a window so the tree really lays out.</summary>
    private (SidebarView Sidebar, Window Window) Open()
    {
        var sidebar = new SidebarView();
        var window = new Window { Width = 320, Height = 500, Content = sidebar };
        window.Show();
        sidebar.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (sidebar, window);
    }

    private static string? Label(Button button) => AutomationProperties.GetName(button);

    private static string? Icon(Button button) => ((LucideIcon)button.Content!).IconKey;

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void The_button_offers_Expand_once_the_tree_is_fully_collapsed_and_Collapse_again_after()
    {
        Touch("Projects/Neural Nets/Attention.md");
        Touch("Inbox.md");
        var (sidebar, _) = Open();
        var toggle = sidebar.FoldersToggle;

        // A freshly opened notebook shows its top level with the folders shut — so the offer is to open them.
        Assert.Equal("Expand all folders", Label(toggle));
        Assert.Equal("chevrons-down", Icon(toggle));

        Click(toggle);

        // Everything is open, so the only thing left to offer is the reverse.
        Assert.Equal("Collapse all folders", Label(toggle));
        Assert.Equal("chevrons-up", Icon(toggle));

        Click(toggle);

        Assert.Equal("Expand all folders", Label(toggle));
        Assert.Equal("chevrons-down", Icon(toggle));
    }

    /// <summary>
    /// The label tracks the tree, not the button's own history: open a folder any other way and the header must
    /// already be offering Collapse by the time you look at it.
    /// </summary>
    [AvaloniaFact]
    public void Opening_a_folder_without_the_button_still_flips_the_button()
    {
        Touch("Projects/Neural Nets/Attention.md");
        var (sidebar, _) = Open();
        var toggle = sidebar.FoldersToggle;
        Assert.Equal("Expand all folders", Label(toggle));

        sidebar.RevealInTree(Path("Projects/Neural Nets/Attention.md")); // opens both folders to show the file

        Assert.Equal("Collapse all folders", Label(toggle));
        Assert.Equal("chevrons-up", Icon(toggle));
    }

    /// <summary>A notebook with no folders has nothing to expand or collapse; the button says so by going dim.</summary>
    [AvaloniaFact]
    public void A_flat_notebook_leaves_the_button_disabled()
    {
        Touch("Inbox.md");
        Touch("cover.png");
        var (sidebar, _) = Open();

        Assert.False(sidebar.FoldersToggle.IsEnabled);
    }

    /// <summary>An empty folder opens to nothing, so it must not claim there is something to collapse.</summary>
    [AvaloniaFact]
    public void An_empty_folder_does_not_make_the_button_offer_Collapse()
    {
        Directory.CreateDirectory(Path("Empty"));
        Touch("Inbox.md");
        var (sidebar, _) = Open();
        var toggle = sidebar.FoldersToggle;

        Assert.False(toggle.IsEnabled); // nothing in the folder to show
        Click(toggle);                  // and so nothing an Expand could put on screen

        Assert.Equal("Expand all folders", Label(toggle));
    }
}
