using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// "This is the current thing" must be said in more than one channel. Colour alone leaves a user with a
/// colour-vision deficiency no signal at all (WCAG 2.2 §1.4.1), and the shell was relying on it for the active
/// sidebar panel.
/// </summary>
public sealed class ActiveStateSignifiersTests
{
    private static IReadOnlyList<Button> Switcher(SidebarView sidebar) =>
        [.. sidebar.GetLogicalDescendants().OfType<Button>().Where(b => b.Tag is string)];

    [AvaloniaFact]
    public void TheActiveNavItem_CarriesANonColourSignifier()
    {
        var sidebar = new SidebarView();
        var buttons = Switcher(sidebar);
        Assert.NotEmpty(buttons);

        var active = buttons.Single(b => (string?)b.Tag == sidebar.ActivePanelId);
        var inactive = buttons.Where(b => (string?)b.Tag != sidebar.ActivePanelId).ToList();
        Assert.NotEmpty(inactive);

        // An underline, not merely a hue: strip the colour out of the comparison and the active item is still
        // distinguishable. It carries the whole non-colour signal now that the row is icons rather than words —
        // a glyph has no weight to thicken — so this assertion is the accessibility of the row, not a detail of it.
        Assert.NotEqual(Brushes.Transparent, active.BorderBrush);
        Assert.All(inactive, b => Assert.Equal(Brushes.Transparent, b.BorderBrush));

        // ...and it is announced, so a screen reader gets the state too.
        Assert.Equal("selected", AutomationProperties.GetHelpText(active));
        Assert.All(inactive, b => Assert.Null(AutomationProperties.GetHelpText(b)));
    }

    [AvaloniaFact]
    public void SwitchingPanels_MovesTheSignifier()
    {
        var sidebar = new SidebarView();
        sidebar.Activate("recent");

        var buttons = Switcher(sidebar);
        var active = buttons.Single(b => (string?)b.Tag == "recent");
        Assert.NotEqual(Brushes.Transparent, active.BorderBrush);
        Assert.All(
            buttons.Where(b => (string?)b.Tag != "recent"),
            b => Assert.Equal(Brushes.Transparent, b.BorderBrush));
    }

    [AvaloniaFact]
    public void NavItems_AreABigEnoughPointerTarget()
    {
        // WCAG 2.2 §2.5.8 puts the floor at 24px; the nav items had no minimum at all and were as tall as their
        // text. Now that they are icons they have no intrinsic size to fall back on at all — nor does any other
        // icon button in the sidebar's chrome — so hold the floor for every one of them, in both dimensions.
        var iconButtons = new SidebarView().GetLogicalDescendants().OfType<Button>()
            .Where(b => b.Content is LucideIcon)
            .ToList();

        Assert.NotEmpty(iconButtons);
        Assert.All(iconButtons, b => Assert.True(
            Math.Max(b.MinHeight, b.Height is double.NaN ? 0 : b.Height) >= 24,
            $"{AutomationProperties.GetName(b)} is too short"));
        Assert.All(iconButtons, b => Assert.True(
            Math.Max(b.MinWidth, b.Width is double.NaN ? 0 : b.Width) >= 24,
            $"{AutomationProperties.GetName(b)} is too narrow"));
    }

    [AvaloniaFact]
    public void SelectionUsesTheOneAccent_NotTwo()
    {
        // The tree's selected row used to fall through to FluentTheme's default *blue* system accent while every
        // other "current thing" in the shell read the orange accent from Tokens — two selection languages at once.
        // The tree must now name its own selection brush, and it must be the app's accent.
        var tree = new Puffinote.Desktop.Controls.FileTreeView();
        var list = tree.GetLogicalDescendants().OfType<ListBox>().Single();

        Assert.NotEmpty(list.Styles); // it styles its own selection rather than inheriting Fluent's

        var brushes = list.Styles
            .SelectMany(s => ((Avalonia.Styling.Style)s).Setters)
            .OfType<Avalonia.Styling.Setter>()
            .Select(setter => setter.Value)
            .OfType<IBrush>()
            .ToList();

        // The bar down the leading edge — selection's whole signal.
        Assert.Contains(Tokens.Dark.Accent, brushes);

        // And not the wash: that one marks the file behind the active tab (FileTreeView.BuildRow), so a row the
        // ListBox merely has selected must not borrow it and light the sidebar in two places at once.
        Assert.DoesNotContain(Tokens.Dark.AccentSubtle, brushes);
    }
}
