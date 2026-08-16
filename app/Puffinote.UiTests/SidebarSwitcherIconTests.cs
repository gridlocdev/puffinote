using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Puffinote.Components.Controls.Icons;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The sidebar's panel switcher is a row of icons rather than four words, with Settings on the end. An icon
/// row is only usable while the glyphs actually resolve and the words survive somewhere a screen reader and a
/// hovering pointer can still find them — which is what these hold.
/// </summary>
public sealed class SidebarSwitcherIconTests
{
    private static IReadOnlyList<Button> Switcher(SidebarView sidebar) =>
        [.. sidebar.GetLogicalDescendants().OfType<Button>().Where(b => b.Tag is string)];

    private static Button Named(SidebarView sidebar, string name) =>
        sidebar.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);

    [AvaloniaFact]
    public void EveryNavItem_IsAGlyph_ThatIsActuallyVendored()
    {
        var buttons = Switcher(new SidebarView());
        Assert.Equal(5, buttons.Count);

        foreach (var button in buttons)
        {
            var glyph = Assert.IsType<LucideIcon>(button.Content); // an icon, not a word
            Assert.NotNull(LucideIcons.Get(glyph.IconKey));        // ...and one that resolves to real geometry
        }
    }

    [AvaloniaFact]
    public void NavItems_KeepTheirWords_AsNameAndTooltip()
    {
        // Dropping the labels is only affordable because the word moves rather than disappears: an icon-only
        // button with no accessible name is unreadable to a screen reader, and unguessable without a tooltip.
        var sidebar = new SidebarView();

        foreach (var (tag, label) in new[]
        {
            ("files", "Files"), ("filter", "Filter"), ("search", "Search"), ("outline", "Outline"),
            ("recent", "Recent"),
        })
        {
            var button = Switcher(sidebar).Single(b => (string?)b.Tag == tag);
            Assert.Equal(label, AutomationProperties.GetName(button));
            Assert.Equal(label, ToolTip.GetTip(button));
        }
    }

    [AvaloniaFact]
    public void ClickingANavItem_ActivatesItsPanel()
    {
        var sidebar = new SidebarView();
        var window = new Window { Content = sidebar };
        window.Show();

        Named(sidebar, "Outline").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("outline", sidebar.ActivePanelId);
    }

    [AvaloniaFact]
    public void Settings_SitsInTheRow_ButIsAnActionRatherThanAFifthPanel()
    {
        var sidebar = new SidebarView();
        var window = new Window { Content = sidebar };
        window.Show();

        var settings = Named(sidebar, "Settings");
        Assert.Equal("settings-2", Assert.IsType<LucideIcon>(settings.Content).IconKey);
        Assert.Null(settings.Tag); // untagged: the switcher identifies its own panels by id, and this isn't one

        int raised = 0;
        sidebar.SettingsRequested += () => raised++;
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, raised);
        Assert.Equal("files", sidebar.ActivePanelId); // ...and clicking it doesn't switch the panel out from under you
    }
}
