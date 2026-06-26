using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace BirdBrain.Components.Theming;

/// <summary>
/// The default BirdBrain-family theme: shared neutral chrome (surfaces, borders, muted text, scrim)
/// seeded with one <em>accent</em> the app passes in. Apps register an instance at startup, e.g.
/// <c>AppTheme.Current = new BirdBrainTheme(Color.FromRgb(0x2A, 0x7D, 0xE1));</c>. Per-variant accents
/// can come later; for now the accent is shared across light and dark. Register via
/// <c>Tokens.Current = new BirdBrainTheme(accent)</c>.
/// </summary>
public sealed class BirdBrainTheme : IAppTheme
{
    /// <summary>A ready-to-use theme with the family's default blue accent.</summary>
    public static IAppTheme Default { get; } = new BirdBrainTheme(Color.FromRgb(0x2A, 0x7D, 0xE1));

    private static readonly IBrush ScrimBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));

    public BirdBrainTheme(Color accent)
    {
        IBrush accentBrush = new ImmutableSolidColorBrush(accent);
        IBrush accentSubtle = new ImmutableSolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));

        Dark = new ThemePalette(
            Accent: accentBrush,
            AccentSubtle: accentSubtle,
            Surface: new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x31)),
            SurfaceBorder: new ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3E)),
            MutedText: new ImmutableSolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            Scrim: ScrimBrush);

        Light = new ThemePalette(
            Accent: accentBrush,
            AccentSubtle: accentSubtle,
            Surface: new ImmutableSolidColorBrush(Colors.White),
            SurfaceBorder: new ImmutableSolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD4)),
            MutedText: new ImmutableSolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A)),
            Scrim: ScrimBrush);
    }

    public ThemePalette Light { get; }

    public ThemePalette Dark { get; }
}
