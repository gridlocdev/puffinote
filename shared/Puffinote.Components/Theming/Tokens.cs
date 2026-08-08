using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Puffinote.Components.Theming;

/// <summary>
/// An app's design-token set: the chrome palette for the light and dark theme variants. Each app in
/// the Puffinote family supplies one (typically a <see cref="PuffinoteTheme"/> seeded with its accent)
/// at startup via <see cref="Tokens.Current"/>.
/// </summary>
public interface IAppTheme
{
    ThemePalette Light { get; }

    ThemePalette Dark { get; }
}

/// <summary>
/// The active design <b>tokens</b> for the running app, plus convenience accessors. Mirrors the
/// editor's existing <c>GridPalette.For(ThemeVariant)</c> pattern so theme-aware chrome can resolve
/// tokens for its current variant. Defaults to <see cref="PuffinoteTheme.Default"/> so a control (or a
/// headless test) constructed before an app registers its theme still gets sensible tokens. (Named
/// <c>Tokens</c> rather than <c>AppTheme</c> to avoid colliding with apps' own theme-preference types.)
/// <para>
/// Chrome should read <see cref="Active"/>, not <see cref="Light"/> or <see cref="Dark"/>: it is the
/// palette for whichever variant the app is <em>currently</em> in, and — because its brushes are one
/// fixed set of mutable <see cref="SolidColorBrush"/>es that get re-tinted in place — a control that
/// took one at construction repaints when the theme changes, without being rebuilt or knowing that a
/// theme exists. <see cref="Light"/>/<see cref="Dark"/> remain for the rare surface that genuinely
/// wants one specific variant regardless of the app's.
/// </para>
/// </summary>
public static class Tokens
{
    /// <summary>
    /// The live palette every piece of chrome paints from — the tokens of the app's current variant.
    /// Built once and re-tinted in place, so the brush a control captured at construction stays the
    /// brush it paints with, and a theme change reaches it as a colour change on that brush.
    /// </summary>
    public static ThemePalette Active { get; } = Mutable(PuffinoteTheme.Default.Dark);

    public static IAppTheme Current
    {
        get;
        set
        {
            field = value;
            Retint();
        }
    } = PuffinoteTheme.Default;

    /// <summary>
    /// The variant <see cref="Active"/> currently reflects. Apps set it from the resolved theme —
    /// <c>Application.ActualThemeVariant</c>, which answers what "follow the system" resolved to —
    /// or let <see cref="Follow"/> keep it in step.
    /// </summary>
    public static ThemeVariant Variant
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Retint();
        }
    } = ThemeVariant.Dark;

    /// <summary>
    /// Raised after <see cref="Active"/> has been re-tinted for a new variant. Only for chrome that
    /// derives a colour from a token rather than painting with the brush itself (a faded copy, a
    /// blend): anything holding a token brush directly is already up to date by the time this fires.
    /// </summary>
    public static event EventHandler? Changed;

    /// <summary>The dark-variant chrome palette, whatever variant the app is in.</summary>
    public static ThemePalette Dark => Current.Dark;

    /// <summary>The light-variant chrome palette, whatever variant the app is in.</summary>
    public static ThemePalette Light => Current.Light;

    /// <summary>The chrome palette for <paramref name="variant"/> (anything but Dark resolves to Light).</summary>
    public static ThemePalette For(ThemeVariant variant) =>
        variant == ThemeVariant.Dark ? Current.Dark : Current.Light;

    /// <summary>
    /// Track <paramref name="app"/>'s resolved variant from now on, and adopt it now. This is what makes
    /// "follow the system" live: with <c>RequestedThemeVariant</c> left at <see cref="ThemeVariant.Default"/>
    /// the OS can flip the app light or dark at any moment (macOS's automatic appearance does it at dusk),
    /// and the chrome has to follow it without anyone opening Preferences.
    /// </summary>
    public static void Follow(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.ActualThemeVariantChanged += (_, _) => Variant = app.ActualThemeVariant;
        Variant = app.ActualThemeVariant;
    }

    /// <summary>Repaint <see cref="Active"/> in the current theme's colours for the current variant.</summary>
    private static void Retint()
    {
        var source = For(Variant);
        Recolor(Active.Accent, source.Accent);
        Recolor(Active.AccentSubtle, source.AccentSubtle);
        Recolor(Active.Surface, source.Surface);
        Recolor(Active.SurfaceHover, source.SurfaceHover);
        Recolor(Active.SurfaceInset, source.SurfaceInset);
        Recolor(Active.SurfaceBorder, source.SurfaceBorder);
        Recolor(Active.Text, source.Text);
        Recolor(Active.MutedText, source.MutedText);
        Recolor(Active.Scrim, source.Scrim);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void Recolor(IBrush target, IBrush source)
    {
        if (target is SolidColorBrush brush && source is ISolidColorBrush colored)
        {
            brush.Color = colored.Color;
        }
    }

    /// <summary>A copy of <paramref name="palette"/> whose every brush is mutable, for re-tinting in place.</summary>
    private static ThemePalette Mutable(ThemePalette palette) => new(
        Accent: Copy(palette.Accent),
        AccentSubtle: Copy(palette.AccentSubtle),
        Surface: Copy(palette.Surface),
        SurfaceHover: Copy(palette.SurfaceHover),
        SurfaceInset: Copy(palette.SurfaceInset),
        SurfaceBorder: Copy(palette.SurfaceBorder),
        Text: Copy(palette.Text),
        MutedText: Copy(palette.MutedText),
        Scrim: Copy(palette.Scrim));

    private static SolidColorBrush Copy(IBrush brush) =>
        new(brush is ISolidColorBrush colored ? colored.Color : Colors.Transparent);
}
