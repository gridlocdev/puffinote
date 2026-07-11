using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Puffinote.Components.Controls;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Every command palette in the app — Puffinote's ⌘K, Typage's ⌘K, the markdown bar's "Find a tool…" flyout,
/// Cellestra's palette — renders its rows through the one <see cref="PaletteListView"/>. These pin the row's
/// glyph placement to the control, so a host that hands over a bare glyph still gets rows that line up: the
/// four surfaces had drifted precisely because three of them left placement to their own icon factory.
/// </summary>
public class PaletteListViewTests
{
    /// <summary>A host icon factory of the sloppiest kind: a sized glyph and nothing else — no alignment, no margin.</summary>
    private static PaletteListView BuiltPalette()
    {
        var palette = new PaletteListView(key => string.IsNullOrEmpty(key)
            ? null
            : new Border { Width = PaletteListView.IconSize, Height = PaletteListView.IconSize, Background = Brushes.Red });

        palette.SetItems([
            new PaletteEntry("Bold", () => { }, "Make the selection bold", "⌘B", IconKey: "bold"),
            new PaletteEntry("Italic", () => { }, "Slant the selection", "⌘I", IconKey: "italic"),
        ]);

        // Realize the template so the rows actually measure/arrange.
        var root = new Window { Width = 560, Height = 400, Content = palette };
        root.Show();
        root.UpdateLayout();
        return palette;
    }

    /// <summary>The first row's glyph and its text block — both children of the row's DockPanel, so their Bounds are directly comparable.</summary>
    private static (Control Icon, Control Text) FirstRow(PaletteListView palette)
    {
        var title = palette.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Bold");
        var dock = title.FindAncestorOfType<DockPanel>()!;
        var icon = dock.Children.OfType<Border>().First(b => Equals(b.Background, Brushes.Red));
        var text = dock.Children.OfType<StackPanel>().First();
        return (icon, text);
    }

    [AvaloniaFact]
    public void RowCentresItsGlyphRatherThanStretchingItDownTheRow()
    {
        var (icon, _) = FirstRow(BuiltPalette());

        // Docked left with no alignment, the glyph stretched to the full height of the title+description
        // block. It must stay its own size, centred against that block.
        Assert.Equal(VerticalAlignment.Center, icon.VerticalAlignment);
        Assert.Equal(PaletteListView.IconSize, icon.Bounds.Height);
    }

    [AvaloniaFact]
    public void RowGivesItsGlyphAGutterSoItIsNotFlushAgainstTheTitle()
    {
        var (icon, text) = FirstRow(BuiltPalette());

        // With no margin the glyph butted straight up against the title.
        double gutter = text.Bounds.Left - icon.Bounds.Right;
        Assert.True(gutter >= 8, $"glyph should sit a gutter clear of the title, but the gap is {gutter:0.#}px");
    }
}
