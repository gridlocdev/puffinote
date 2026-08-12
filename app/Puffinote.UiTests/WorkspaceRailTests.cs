using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The rail down the far-left edge of the window (PRD §7.1): four workspaces, of which one is built. What
/// is worth pinning is that the rail says which one you are in without relying on colour, that switching
/// away and back doesn't rebuild the notebook underneath, and that every destination is reachable by
/// keyboard through the command catalog as well as by pointer.
/// </summary>
public sealed class WorkspaceRailTests
{
    private static MainWindow Open()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void It_offers_the_four_workspaces_in_order_and_starts_in_the_notebook()
    {
        var window = Open();

        Assert.Equal(["Todos", "Calendar", "Notes", "Data"], window.WorkspaceRailForTest.TitlesForTest);
        Assert.Equal(Workspace.Notes, window.ActiveWorkspace);

        // Marked with a shape and spoken, not merely coloured (WCAG 2.2 §1.4.1).
        Assert.Equal("Notes", window.WorkspaceRailForTest.MarkedTitleForTest);
    }

    [AvaloniaFact]
    public void Every_entry_has_a_glyph_that_actually_exists()
    {
        // The icon set is the vendored Lucide subset, and a key with no SVG behind it resolves to null and
        // draws *nothing* — no error, no placeholder, just a word with a hole above it, which is how Notes
        // first shipped. An entry names the glyph it wants and the glyph is vendored to match; this is what
        // says the file is really there, since looking at the window is what this suite cannot do.
        foreach (var workspace in Workspaces.All)
        {
            Assert.True(
                LucideIcons.Get(workspace.Icon) is not null,
                $"{workspace.Title}'s icon \"{workspace.Icon}\" is not vendored under Assets/icons.");
        }
    }

    /// <summary>The presenter the button's template paints its wash and its leading bar on.</summary>
    private static ContentPresenter Face(MainWindow window, string title) =>
        window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == title)
            .GetVisualDescendants().OfType<ContentPresenter>().First();

    private static void Hover(MainWindow window, string title, bool hovering)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == title);
        ((IPseudoClasses)button.Classes).Set(":pointerover", hovering);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void The_entry_you_are_in_does_not_react_to_the_pointer()
    {
        // Hovering it used to swap the accent wash for the theme's grey *and* drop the accent bar, because
        // FluentTheme's :pointerover setters land on the template and outrank a brush set on the button. A
        // destination you have already arrived at should not offer to take you there.
        var window = Open();
        window.WorkspaceRailForTest.ClickForTest("Calendar");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var resting = Face(window, "Calendar");
        var (wash, bar) = (resting.Background, resting.BorderBrush);
        Assert.NotEqual(Brushes.Transparent, wash); // it is washed and barred to begin with
        Assert.NotEqual(Brushes.Transparent, bar);

        Hover(window, "Calendar", hovering: true);
        Assert.Same(wash, Face(window, "Calendar").Background);
        Assert.Same(bar, Face(window, "Calendar").BorderBrush);
    }

    [AvaloniaFact]
    public void The_other_entries_still_light_up_under_the_pointer()
    {
        // The fix must not cost the three you *can* go to their hover: there the pointer is telling the truth.
        var window = Open(); // Notes is active, so Todos is one of the three
        window.UpdateLayout();

        var resting = Face(window, "Todos").Background;
        Hover(window, "Todos", hovering: true);
        var hovered = Face(window, "Todos").Background;

        Assert.NotEqual(resting, hovered);
        Assert.Same(Tokens.Active.SurfaceHover, hovered); // the shell's ordinary hover wash, not the theme's

        Hover(window, "Todos", hovering: false);
        Assert.Equal(resting, Face(window, "Todos").Background);
    }

    [AvaloniaFact]
    public void Choosing_a_workspace_that_is_not_built_says_so_rather_than_showing_a_blank_page()
    {
        var window = Open();
        window.WorkspaceRailForTest.ClickForTest("Calendar");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Workspace.Calendar, window.ActiveWorkspace);
        Assert.Equal("Calendar", window.WorkspaceRailForTest.MarkedTitleForTest);

        var placeholder = window.GetVisualDescendants().OfType<WorkspacePlaceholder>().Single();
        var lines = placeholder.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("Calendar", lines);
        Assert.Contains("Not built yet.", lines);

        // And the notebook is genuinely off screen while it is.
        Assert.Empty(window.GetVisualDescendants().OfType<SidebarView>());
    }

    [AvaloniaFact]
    public void Going_back_to_Notes_returns_the_same_notebook_rather_than_a_new_one()
    {
        // Switching workspaces must not tear the notebook down: an open tab, a scroll position and a
        // selection all live in those controls, and rebuilding them would quietly throw the lot away.
        var window = Open();
        var sidebar = window.SidebarForTest;

        window.WorkspaceRailForTest.ClickForTest("Todos");
        Dispatcher.UIThread.RunJobs();
        window.WorkspaceRailForTest.ClickForTest("Notes");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Workspace.Notes, window.ActiveWorkspace);
        Assert.Same(sidebar, window.GetVisualDescendants().OfType<SidebarView>().Single());
    }

    [AvaloniaFact]
    public void Every_workspace_is_reachable_from_the_command_catalog()
    {
        // A rail is a fine way to move between things you can see. The Command Menu is how you reach them
        // without one — and how a keyboard reaches them at all.
        var window = Open();
        var ids = window.ShellCommandsForTest.Select(c => c.Id).ToList();

        foreach (var workspace in Workspaces.All)
        {
            Assert.Contains(Workspaces.CommandId(workspace.Id), ids);
        }

        window.ShellCommandsForTest.First(c => c.Id == Workspaces.CommandId(Workspace.Data)).Execute();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Workspace.Data, window.ActiveWorkspace);
    }
}
