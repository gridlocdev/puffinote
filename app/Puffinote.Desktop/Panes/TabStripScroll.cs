namespace Puffinote.Desktop.Panes;

/// <summary>
/// Where the tab strip has to be scrolled to for the active tab to be on screen. Split out from
/// <see cref="TabHost"/> because the interesting part is arithmetic on edges — a tab half off either side, a
/// tab wider than the strip, a strip with nothing to scroll — and that is worth testing without a window.
/// </summary>
internal static class TabStripScroll
{
    /// <summary>
    /// The horizontal offset that shows the tab spanning
    /// <paramref name="tabLeft"/>…<paramref name="tabLeft"/> + <paramref name="tabWidth"/> (content
    /// coordinates), starting from <paramref name="offset"/>. Scrolls the smallest distance that does it, so a
    /// tab already fully visible doesn't move the strip at all; a tab too wide to fit lines up with its left
    /// edge, because that is the end carrying the filename.
    /// </summary>
    internal static double Reveal(double offset, double viewportWidth, double extentWidth, double tabLeft, double tabWidth)
    {
        if (viewportWidth <= 0)
        {
            return offset; // not laid out (or the strip is hidden) — nothing meaningful to scroll to yet
        }

        var target = offset;
        if (tabLeft + tabWidth > target + viewportWidth)
        {
            target = tabLeft + tabWidth - viewportWidth;
        }

        // Second, so it wins for a tab wider than the viewport: showing the start of the title beats showing
        // its trailing edge and the close button.
        if (tabLeft < target)
        {
            target = tabLeft;
        }

        var furthest = Math.Max(0, extentWidth - viewportWidth);
        return Math.Clamp(target, 0, furthest);
    }
}
