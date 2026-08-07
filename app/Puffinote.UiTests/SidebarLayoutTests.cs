using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

public sealed class SidebarLayoutTests
{
    private static (SidebarLayout Layout, ColumnDefinition Column, Border Sidebar, GridSplitter Splitter, Border Handle) Build()
    {
        var column = new ColumnDefinition(260, GridUnitType.Pixel) { MinWidth = 160 };
        var sidebar = new Border();
        var splitter = new GridSplitter();
        var handle = new Border();
        return (new SidebarLayout(column, sidebar, splitter, handle, defaultWidth: 260), column, sidebar, splitter, handle);
    }

    [AvaloniaFact]
    public void Toggle_collapses_then_restores_the_width_it_was_collapsed_from()
    {
        var (layout, column, sidebar, splitter, handle) = Build();
        column.Width = new GridLength(320, GridUnitType.Pixel); // user widened the sidebar before hiding it

        layout.Toggle(); // collapse
        Assert.True(layout.IsCollapsed);
        Assert.Equal(0, column.Width.Value);
        Assert.Equal(0, column.MinWidth);
        Assert.False(sidebar.IsVisible);
        Assert.False(splitter.IsVisible);
        Assert.True(handle.IsVisible);

        layout.Toggle(); // expand — restores the remembered live width, not the default
        Assert.False(layout.IsCollapsed);
        Assert.Equal(320, column.Width.Value);
        Assert.Equal(160, column.MinWidth);
        Assert.True(sidebar.IsVisible);
        Assert.True(splitter.IsVisible);
        Assert.False(handle.IsVisible);
    }

    /// <summary>
    /// The whole point of the handle: a sidebar hidden by its own header button would otherwise take its
    /// only pointer affordance with it, leaving the menu or ⌘\ as the sole way back.
    /// </summary>
    [AvaloniaFact]
    public void The_handle_shows_only_while_the_sidebar_is_hidden()
    {
        var (layout, _, _, _, handle) = Build();
        Assert.False(handle.IsVisible); // starts expanded

        layout.Collapse();
        Assert.True(handle.IsVisible);

        layout.Expand();
        Assert.False(handle.IsVisible);
    }

    [AvaloniaFact]
    public void Collapse_and_expand_are_idempotent_and_dont_forget_the_width()
    {
        var (layout, column, _, _, _) = Build();
        column.Width = new GridLength(320, GridUnitType.Pixel);

        layout.Collapse();
        layout.Collapse(); // a second ask must not remember the now-zero width as the user's
        Assert.Equal(320, layout.PersistedWidth);

        layout.Expand();
        layout.Expand();
        Assert.False(layout.IsCollapsed);
        Assert.Equal(320, column.Width.Value);
    }

    [AvaloniaFact]
    public void Apply_restores_a_persisted_collapsed_state_and_later_expands_to_the_saved_width()
    {
        var (layout, column, sidebar, _, handle) = Build();

        layout.Apply(collapsed: true, width: 300);
        Assert.True(layout.IsCollapsed);
        Assert.False(sidebar.IsVisible);
        Assert.True(handle.IsVisible);
        Assert.Equal(300, layout.PersistedWidth); // the saved width survives even while collapsed

        layout.Apply(collapsed: false, width: 300);
        Assert.False(layout.IsCollapsed);
        Assert.Equal(300, column.Width.Value);
    }

    [AvaloniaFact]
    public void Apply_falls_back_to_the_default_width_when_the_saved_width_is_non_positive()
    {
        var (layout, column, _, _, _) = Build();
        layout.Apply(collapsed: false, width: 0);
        Assert.Equal(260, column.Width.Value);
    }

    [AvaloniaFact]
    public void PersistedWidth_reports_the_live_column_width_while_expanded()
    {
        var (layout, column, _, _, _) = Build();
        column.Width = new GridLength(275, GridUnitType.Pixel);
        Assert.Equal(275, layout.PersistedWidth);
    }
}
