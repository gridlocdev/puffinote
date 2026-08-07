using Avalonia.Controls;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// Owns the sidebar's collapse/expand mechanics — the grid column, its border, the resize splitter, and the
/// collapsed-state handle — so the call sites that toggle, restore, and read the layout (PRD §7.1) share one
/// implementation instead of each poking the controls directly. Collapsing hides the column (width 0,
/// min-width 0) after remembering the last expanded width, so expanding restores it and the splitter reappears.
///
/// <para>PRD §7.2.1 allows collapsing "to a zero/handle width": the sidebar itself goes to zero, and a narrow
/// <em>handle</em> takes its place — visible only while collapsed — so the control that hid the sidebar is
/// still there, in the same corner, to bring it back. Without it, a collapsed sidebar could only be recovered
/// from the menu or ⌘\.</para>
/// </summary>
internal sealed class SidebarLayout
{
    private const double MinExpandedWidth = 160;

    private readonly ColumnDefinition _column;
    private readonly Control _sidebar;
    private readonly Control _splitter;
    private readonly Control _handle;
    private readonly double _defaultWidth;
    private GridLength _lastWidth;

    public SidebarLayout(ColumnDefinition column, Control sidebar, Control splitter, Control handle, double defaultWidth)
    {
        _column = column;
        _sidebar = sidebar;
        _splitter = splitter;
        _handle = handle;
        _defaultWidth = defaultWidth;
        _lastWidth = new GridLength(defaultWidth, GridUnitType.Pixel);
        Show(); // the layout starts expanded; keep the handle in step with that from the first frame
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
            Collapse();
        }
    }

    /// <summary>Hide the sidebar, remembering the width the user left it at. No-op when already collapsed.</summary>
    public void Collapse()
    {
        if (IsCollapsed)
        {
            return;
        }

        _lastWidth = _column.Width; // remember where the user left it before hiding
        Hide();
    }

    /// <summary>Show the sidebar again at its remembered width. No-op when already expanded.</summary>
    public void Expand()
    {
        if (!IsCollapsed)
        {
            return;
        }

        Show();
    }

    /// <summary>
    /// Restore a persisted state (PRD §7.1): remember <paramref name="width"/> (falling back to the default
    /// when it's non-positive), then collapse or expand to match <paramref name="collapsed"/>. Unlike
    /// <see cref="Collapse"/>/<see cref="Expand"/> this always re-applies, so restoring a session can't be
    /// swallowed by the layout happening to already be in that state with a stale width.
    /// </summary>
    public void Apply(bool collapsed, double width)
    {
        _lastWidth = new GridLength(width > 0 ? width : _defaultWidth, GridUnitType.Pixel);
        if (collapsed)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    private void Hide()
    {
        _column.Width = new GridLength(0);
        _column.MinWidth = 0;
        _sidebar.IsVisible = false;
        _splitter.IsVisible = false;
        _handle.IsVisible = true; // the only way back, once the sidebar's own header is gone
    }

    private void Show()
    {
        _column.MinWidth = MinExpandedWidth;
        _column.Width = _lastWidth;
        _sidebar.IsVisible = true;
        _splitter.IsVisible = true;
        _handle.IsVisible = false;
    }
}
