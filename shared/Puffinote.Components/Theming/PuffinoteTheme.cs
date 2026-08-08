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

        // Text is the primary ink — a file's name in the tree, the notebook's name in the sidebar header. It was
        // missing, and the things that needed it reached for MutedText instead, so a panel whose whole job is to
        // list names wrote every one of them at the level meant for the hint beside it. #E6E9ED is ~13:1 on the
        // dark Surface below and stays a clear step above MutedText, which is what keeps "secondary" meaningful.
        //
        // MutedText carries real information — inactive tab titles, a file's extension, the recent list's folder
        // line — so it has to clear WCAG 1.4.3's 4.5:1 for body text, not merely look secondary. The old #888888 sat at
        // ~4.7:1 on the #252526 chrome: passing on paper, but with no margin, and it fell *below* threshold
        // anywhere the surface lightened. #9BA1A6 lifts it to ~6.5:1 while still reading a clear step back from
        // the primary text. SurfaceBorder likewise has to clear 3:1 as a UI boundary (1.4.11), not just be visible.
        // SurfaceHover steps *up* off Surface (the pointer's wash on a row); SurfaceInset steps *down* into it
        // (the well behind a badge). Hover was previously being painted with SurfaceBorder for want of a token,
        // which is a border weight — far too loud as a fill.
        //
        // SurfaceInset going darker rather than lighter is what lets MutedText sit on a badge and stay legible:
        // #9BA1A6 on this well is ~6.1:1, and because the well is opaque it holds that ratio over a hovered row,
        // a selected row, and the accent wash alike. A *lighter* chip would have landed at ~4.2:1 on the same
        // text and drifted further with every state the row underneath can be in.
        Dark = new ThemePalette(
            Accent: accentBrush,
            AccentSubtle: accentSubtle,
            Surface: new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x31)),
            SurfaceHover: new ImmutableSolidColorBrush(Color.FromRgb(0x34, 0x37, 0x3E)),
            SurfaceInset: new ImmutableSolidColorBrush(Color.FromRgb(0x20, 0x22, 0x28)),
            SurfaceBorder: new ImmutableSolidColorBrush(Color.FromRgb(0x4C, 0x4F, 0x55)),
            Text: new ImmutableSolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xED)),
            MutedText: new ImmutableSolidColorBrush(Color.FromRgb(0x9B, 0xA1, 0xA6)),
            Scrim: ScrimBrush);

        Light = new ThemePalette(
            Accent: accentBrush,
            AccentSubtle: accentSubtle,
            Surface: new ImmutableSolidColorBrush(Colors.White),
            SurfaceHover: new ImmutableSolidColorBrush(Color.FromRgb(0xEF, 0xF1, 0xF4)),
            SurfaceInset: new ImmutableSolidColorBrush(Color.FromRgb(0xEC, 0xEE, 0xF1)),
            SurfaceBorder: new ImmutableSolidColorBrush(Color.FromRgb(0xBF, 0xC3, 0xC9)),
            Text: new ImmutableSolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)),
            MutedText: new ImmutableSolidColorBrush(Color.FromRgb(0x5C, 0x63, 0x6B)),
            Scrim: ScrimBrush);
    }

    public ThemePalette Light { get; }

    public ThemePalette Dark { get; }
}
