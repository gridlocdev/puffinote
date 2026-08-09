using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The palette opens at one of two distances, deliberately. Clicking the search pill drops it from the pill:
/// the click started there, so the eye is already in the top-right corner and the results should meet it there.
/// ⌘K opens it centred instead — a chord is fired from a neutral, centred gaze, with no pointer to anchor to.
/// </summary>
public class CommandPaletteSurfaceTests
{
    /// <summary>
    /// A shown window, settled. MainWindow.OnOpened focuses the grid once, on open; let that land before the
    /// test drives anything, or it arrives late and steals the focus back from whatever the test just opened.
    /// </summary>
    private static MainWindow ShownWindow()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button SearchPill(MainWindow window) =>
        window.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Search commands…"));

    private static OverlayHost Overlay(MainWindow window) =>
        window.GetVisualDescendants().OfType<OverlayHost>().First();

    /// <summary>The rows currently listed by a palette (its ListBox is its own logical child, so no popup realization needed).</summary>
    private static IReadOnlyList<object> Rows(PaletteListView palette) =>
        [.. palette.Children.OfType<ListBox>().Single().ItemsSource?.Cast<object>() ?? []];

    [AvaloniaFact]
    public void Clicking_the_search_pill_drops_the_palette_from_the_pill()
    {
        var window = ShownWindow();

        // A flyout on the pill is what makes it drop from the pill — the shell prefers it over any OnClick.
        var flyout = Assert.IsType<Flyout>(SearchPill(window).Flyout);
        flyout.ShowAt(SearchPill(window));
        Dispatcher.UIThread.RunJobs();

        var palette = Assert.IsType<PaletteListView>(Assert.IsType<Border>(flyout.Content).Child);
        Assert.NotEmpty(Rows(palette));

        // The search box must take focus on open. The dropped palette doesn't go through the OverlayHost, so
        // nothing else is holding the keyboard off the grid — if focus stayed put, typing a command's name would
        // land in the active cell and start editing it.
        Assert.True(palette.SearchBox.IsFocused);
        Assert.False(window.Editor.Grid.IsFocused);

        // ...and the centred overlay stayed shut: a click gets the anchored surface, not the middle of the window.
        Assert.False(Overlay(window).IsOpen);
    }

    [AvaloniaFact]
    public void The_shortcut_still_opens_the_palette_centred()
    {
        var window = ShownWindow();
        IAppEnvironment env = window.Editor;

        env.ShowCommandPalette(); // what ⌘K (view.commandMenu) runs
        Dispatcher.UIThread.RunJobs();

        var overlay = Overlay(window);
        Assert.True(overlay.IsOpen);

        var palette = overlay.GetVisualDescendants().OfType<PaletteListView>().Single();
        Assert.NotEmpty(Rows(palette));

        // The card is centred over the window, not hung off the pill in the corner.
        var card = Assert.IsType<Border>(palette.Parent);
        Assert.Equal(HorizontalAlignment.Center, card.HorizontalAlignment);
    }

    [AvaloniaFact]
    public void The_dropped_palette_is_rebuilt_on_every_open()
    {
        var window = ShownWindow();

        var pill = SearchPill(window);
        var flyout = (Flyout)pill.Flyout!;
        var palette = (PaletteListView)((Border)flyout.Content!).Child!;

        flyout.ShowAt(pill);
        Dispatcher.UIThread.RunJobs();
        int first = Rows(palette).Count;
        Assert.NotEqual(0, first);

        // Type a query, then close without picking anything.
        palette.SearchBox.Text = "zzzzz-no-such-command";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Rows(palette));
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();

        // Reopening must re-ask for the rows rather than reuse a list captured at construction: what's enabled
        // tracks the selection, and a stale query must not still be filtering the list.
        flyout.ShowAt(pill);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Empty, palette.SearchBox.Text);
        Assert.Equal(first, Rows(palette).Count);
    }
}
