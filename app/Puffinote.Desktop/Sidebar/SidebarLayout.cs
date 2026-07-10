using Avalonia.Controls;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// Owns the sidebar's collapse/expand mechanics — the grid column, its border, and the resize splitter — so
/// the three call sites that toggle, restore, and read the layout (PRD §7.1) share one implementation instead
/// of each poking the controls directly. Collapsing hides the column (width 0, min-width 0) after remembering
/// the last expanded width, so expanding restores it and the splitter reappears.
/// </summary>
internal sealed class SidebarLayout
{
    private const double MinExpandedWidth = 160;

    private readonly ColumnDefinition _column;
    private readonly Control _sidebar;
    private readonly Control _splitter;
    private readonly double _defaultWidth;
    private GridLength _lastWidth;

    public SidebarLayout(ColumnDefinition column, Control sidebar, Control splitter, double defaultWidth)
    {
        _column = column;
        _sidebar = sidebar;
        _splitter = splitter;
        _defaultWidth = defaultWidth;
        _lastWidth = new GridLength(defaultWidth, GridUnitType.Pixel);
    }

    /// <summary>True when the sidebar is currently hidden.</summary>
    public bool IsCollapsed => !_sidebar.IsVisible;

    /// <summary>The width to persist (PRD §7.1): the live column width when expanded, else the remembered one.</summary>
    public double PersistedWidth => (IsCollapsed ? _lastWidth : _column.Width).Value;

    /// <summary>Toggle between collapsed and expanded, remembering the live width when collapsing.</summary>
    public void Toggle()
    {
        if (IsCollapsed)
        {
            Expand();
        }
        else
        {
            _lastWidth = _column.Width; // remember where the user left it before hiding
            Collapse();
        }
    }

    /// <summary>
    /// Restore a persisted state (PRD §7.1): remember <paramref name="width"/> (falling back to the default
    /// when it's non-positive), then collapse or expand to match <paramref name="collapsed"/>.
    /// </summary>
    public void Apply(bool collapsed, double width)
    {
        _lastWidth = new GridLength(width > 0 ? width : _defaultWidth, GridUnitType.Pixel);
        if (collapsed)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    private void Collapse()
    {
        _column.Width = new GridLength(0);
        _column.MinWidth = 0;
        _sidebar.IsVisible = false;
        _splitter.IsVisible = false;
    }

    private void Expand()
    {
        _column.MinWidth = MinExpandedWidth;
        _column.Width = _lastWidth;
        _sidebar.IsVisible = true;
        _splitter.IsVisible = true;
    }
}
