using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Puffinote.Components.Theming;

/// <summary>
/// The default Puffinote-family theme: shared neutral chrome (surfaces, borders, muted text, scrim)
/// seeded with one <em>accent</em> the app passes in. Apps register an instance at startup, e.g.
/// <c>AppTheme.Current = new PuffinoteTheme(Color.FromRgb(0x2A, 0x7D, 0xE1));</c>. Per-variant accents
/// can come later; for now the accent is shared across light and dark. Register via
/// <c>Tokens.Current = new PuffinoteTheme(accent)</c>.
/// </summary>
public sealed class PuffinoteTheme : IAppTheme
{
    /// <summary>A ready-to-use theme with the family's default blue accent.</summary>
    public static IAppTheme Default { get; } = new PuffinoteTheme(Color.FromRgb(0x2A, 0x7D, 0xE1));

    private static readonly IBrush ScrimBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));

    public PuffinoteTheme(Color accent)
    {
        IBrush accentBrush = new ImmutableSolidColorBrush(accent);
        IBrush accentSubtle = new ImmutableSolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));

        // MutedText carries real information — inactive tab titles, file names in the tree, the sidebar header —
        // so it has to clear WCAG 1.4.3's 4.5:1 for body text, not merely look secondary. The old #888888 sat at
        // ~4.7:1 on the #252526 chrome: passing on paper, but with no margin, and it fell *below* threshold
        // anywhere the surface lightened. #9BA1A6 lifts it to ~6.5:1 while still reading a clear step back from
        // the primary text. SurfaceBorder likewise has to clear 3:1 as a UI boundary (1.4.11), not just be visible.
        Dark = new ThemePalette(
            Accent: accentBrush,
            AccentSubtle: accentSubtle,
            Surface: new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x31)),
            SurfaceBorder: new ImmutableSolidColorBrush(Color.FromRgb(0x4C, 0x4F, 0x55)),
            MutedText: new ImmutableSolidColorBrush(Color.FromRgb(0x9B, 0xA1, 0xA6)),
            Scrim: ScrimBrush);

        Light = new ThemePalette(
            Accent: accentBrush,
            AccentSubtle: accentSubtle,
            Surface: new ImmutableSolidColorBrush(Colors.White),
            SurfaceBorder: new ImmutableSolidColorBrush(Color.FromRgb(0xBF, 0xC3, 0xC9)),
            MutedText: new ImmutableSolidColorBrush(Color.FromRgb(0x5C, 0x63, 0x6B)),
            Scrim: ScrimBrush);
    }

    public ThemePalette Light { get; }

    public ThemePalette Dark { get; }
}
