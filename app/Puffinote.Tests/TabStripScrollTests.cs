using Puffinote.Desktop.Panes;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The arithmetic behind "the tab you just opened is on screen": every edge case is a tab sitting somewhere
/// awkward relative to the strip's viewport, which is exactly what a window can't show you cheaply.
/// </summary>
public class TabStripScrollTests
{
    // A 400px-wide strip holding 1000px of tabs, currently scrolled 200px in.
    private const double Viewport = 400;
    private const double Extent = 1000;

    private static double Reveal(double offset, double tabLeft, double tabWidth) =>
        TabStripScroll.Reveal(offset, Viewport, Extent, tabLeft, tabWidth);

    [Fact]
    public void A_tab_already_fully_visible_does_not_move_the_strip()
    {
        // Sitting inside the viewport (200…600), including flush against either edge.
        Assert.Equal(200, Reveal(200, tabLeft: 300, tabWidth: 100));
        Assert.Equal(200, Reveal(200, tabLeft: 200, tabWidth: 100));
        Assert.Equal(200, Reveal(200, tabLeft: 500, tabWidth: 100));
    }

    [Fact]
    public void A_tab_off_the_right_scrolls_just_far_enough_to_show_its_trailing_edge()
    {
        // Ends at 750; the shortest scroll puts 750 at the right edge of the viewport, not the left.
        Assert.Equal(350, Reveal(200, tabLeft: 650, tabWidth: 100));
    }

    [Fact]
    public void A_tab_off_the_left_scrolls_back_to_its_leading_edge()
    {
        Assert.Equal(120, Reveal(200, tabLeft: 120, tabWidth: 100));
    }

    [Fact]
    public void A_tab_only_half_on_screen_still_counts_as_needing_the_scroll()
    {
        Assert.Equal(250, Reveal(200, tabLeft: 550, tabWidth: 100)); // right edge 650, viewport ends 600
        Assert.Equal(150, Reveal(200, tabLeft: 150, tabWidth: 100)); // left edge 150, viewport starts 200
    }

    [Fact]
    public void A_tab_wider_than_the_strip_lines_up_with_its_title_end()
    {
        // Both edges can't be shown, so the left one wins — that's where the filename is.
        Assert.Equal(300, Reveal(200, tabLeft: 300, tabWidth: 700));
    }

    [Fact]
    public void The_strip_never_scrolls_past_either_end()
    {
        // Clamped at 0 rather than dragged negative by a tab that starts before the content does.
        Assert.Equal(0, Reveal(50, tabLeft: -20, tabWidth: 100));

        // ...and at extent - viewport (600) rather than scrolled into empty space past the last tab.
        Assert.Equal(600, Reveal(200, tabLeft: 900, tabWidth: 100));
        Assert.Equal(600, TabStripScroll.Reveal(700, Viewport, Extent, tabLeft: 950, tabWidth: 50));
    }

    [Fact]
    public void A_strip_with_nothing_to_scroll_stays_put()
    {
        // Content narrower than the viewport: every tab is visible, so no offset is ever warranted.
        Assert.Equal(0, TabStripScroll.Reveal(0, Viewport, extentWidth: 250, tabLeft: 150, tabWidth: 100));
    }

    [Fact]
    public void An_unlaid_out_strip_is_left_alone()
    {
        // Zero viewport means the strip is hidden or hasn't been measured yet; guessing an offset here would
        // just be a scroll the user has to undo once it does appear.
        Assert.Equal(200, TabStripScroll.Reveal(200, viewportWidth: 0, extentWidth: 0, tabLeft: 900, tabWidth: 100));
    }
}
