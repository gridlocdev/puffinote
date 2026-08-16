using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Every button in the sidebar's icon row is also a command (PRD §7.4). The row is a fine way to move between
/// things you can see; it is no way to move between things you have to recognise by glyph, and it is no way
/// at all to move by keyboard. What is worth pinning is that the catalog covers the whole row rather than
/// whichever panels somebody remembered, that the commands are named for the buttons they stand for, and that
/// asking for a panel actually leaves you looking at one — even when the sidebar was hidden.
/// </summary>
public sealed class SidebarCommandTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-sidebar-commands");

    public void Dispose() => _notebook.Delete(recursive: true);

    private MainWindow Open()
    {
        File.WriteAllText(System.IO.Path.Combine(_notebook.FullName, "Inbox.md"), "");

        var window = new MainWindow(new NotebookService()) { Width = 900, Height = 600 };
        window.Show();
        window.OpenNotebook(_notebook.FullName);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void The_whole_row_is_in_the_catalog_named_after_its_buttons()
    {
        var window = Open();
        var titles = window.ShellCommandsForTest.Select(c => c.Title).ToList();

        // The five panels and the Settings button at the end of the row — every glyph the row draws.
        Assert.Equal(
            ["Files (Sidebar)", "Filter (Sidebar)", "Search (Sidebar)", "Outline (Sidebar)", "Recent (Sidebar)"],
            window.SidebarForTest.Panels.Select(p => $"{p.Title} (Sidebar)"));

        foreach (var expected in new[]
        {
            "Files (Sidebar)", "Filter (Sidebar)", "Search (Sidebar)",
            "Outline (Sidebar)", "Recent (Sidebar)",
        })
        {
            Assert.Contains(expected, titles);
        }

        // …and the Settings button carries no suffix. On a panel it disambiguates — it says which Search
        // you are about to get. On this one it read as a claim about the subject: "Settings (Sidebar)"
        // looks like settings *for* the sidebar, which is a screen this app does not have.
        Assert.Contains("Settings", titles);
        Assert.DoesNotContain("Settings (Sidebar)", titles);
    }

    [AvaloniaFact]
    public void A_command_is_named_for_the_button_it_stands_for()
    {
        // One name for one thing: what the row's tooltip says is what the Command Menu offers, minus the
        // suffix that says which of the app's several Searches this one is.
        var window = Open();
        var titles = window.ShellCommandsForTest.Select(c => c.Title).ToList();

        var buttons = window.SidebarForTest.GetLogicalDescendants().OfType<Button>()
            .Where(b => b.Tag is string)
            .Select(b => AutomationProperties.GetName(b) ?? string.Empty);

        foreach (var button in buttons)
        {
            Assert.Contains($"{button} (Sidebar)", titles);
        }
    }

    [AvaloniaTheory]
    [InlineData("files")]
    [InlineData("filter")]
    [InlineData("search")]
    [InlineData("outline")]
    [InlineData("recent")]
    public void Running_one_shows_that_panel(string panelId)
    {
        var window = Open();
        window.SidebarForTest.Activate("outline"); // start somewhere that isn't the answer for most cases

        window.InvokeShellForTest("view.sidebar." + panelId);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(panelId, window.SidebarForTest.ActivePanelId);
    }

    [AvaloniaFact]
    public void Asking_for_a_panel_reveals_a_hidden_sidebar_rather_than_switching_behind_it()
    {
        // Otherwise the command reads as one that did nothing: you asked to see Search and the window
        // looks exactly as it did.
        var window = Open();
        window.InvokeShellForTest("shell.toggleSidebar");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsSidebarCollapsedForTest);

        window.InvokeShellForTest("view.sidebar.search");
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsSidebarCollapsedForTest);
        Assert.Equal("search", window.SidebarForTest.ActivePanelId);
    }

    [AvaloniaFact]
    public void Reaching_Filter_by_command_lands_the_caret_in_its_box()
    {
        // The point of reaching a typing panel from the keyboard: you can start typing.
        var window = Open();

        window.InvokeShellForTest("view.sidebar.filter");
        Dispatcher.UIThread.RunJobs();

        var box = window.FocusManager?.GetFocusedElement() as TextBox;
        Assert.NotNull(box);
        Assert.Equal("Filter by name…", box.PlaceholderText);
    }

    [AvaloniaFact]
    public void Settings_opens_Preferences_the_way_its_button_does()
    {
        var window = Open();

        window.InvokeShellForTest("view.sidebar.settings");
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsOverlayOpenForTest);
    }
}
