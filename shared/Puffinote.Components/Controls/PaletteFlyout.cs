using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Puffinote.Components.Controls;

/// <summary>
/// A command palette that drops from the control it is attached to, rather than opening centred over the
/// window. This is the pointer half of the two-distance palette: a click starts at the search pill in the top
/// right, so the results should open under the pointer — where the eye already is. The keyboard half (⌘K) has
/// no such anchor, so hosts keep opening their centred overlay for it, where the eye rests by default.
/// </summary>
public static class PaletteFlyout
{
    /// <summary>The dropped palette's width — narrower than a centred overlay card, since it hangs off a pill.</summary>
    private const double Width = 380;

    /// <summary>
    /// Build the pill's palette flyout.
    /// </summary>
    /// <param name="entries">
    /// Supplies the rows, and is re-invoked on every open — command availability tracks the selection/caret, so
    /// a list captured once at construction would go stale. Re-populating also clears the previous query, so the
    /// pill always opens on a clean, complete list.
    /// </param>
    /// <param name="placeholder">The search box's placeholder text.</param>
    /// <param name="iconFactory">Builds a row's glyph (see <see cref="PaletteListView"/>); omit for icon-less rows.</param>
    public static Flyout Anchored(
        Func<IReadOnlyList<PaletteEntry>> entries,
        string placeholder,
        Func<string?, Control?>? iconFactory = null)
    {
        var palette = new PaletteListView(iconFactory) { Placeholder = placeholder };

        var flyout = new Flyout
        {
            Content = new Border { Width = Width, Padding = new Thickness(10), Child = palette },
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard,
        };

        palette.Chosen += entry =>
        {
            flyout.Hide();
            entry.Run();
        };
        palette.Cancelled += (_, _) => flyout.Hide();
        flyout.Opened += (_, _) =>
        {
            palette.SetItems(entries());
            // Defer focus until the popup is realized, otherwise focusing the just-shown box can silently fail
            // and the user has to click into it before they can type.
            Dispatcher.UIThread.Post(() => palette.SearchBox.Focus());
        };

        return flyout;
    }
}
