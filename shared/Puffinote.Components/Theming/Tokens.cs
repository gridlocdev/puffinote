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
/// </summary>
public static class Tokens
{
    public static IAppTheme Current { get; set; } = PuffinoteTheme.Default;

    /// <summary>The dark-variant chrome palette (the Puffinote overlays are dark by default).</summary>
    public static ThemePalette Dark => Current.Dark;

    public static ThemePalette Light => Current.Light;

    /// <summary>The chrome palette for <paramref name="variant"/> (anything but Dark resolves to Light).</summary>
    public static ThemePalette For(ThemeVariant variant) =>
        variant == ThemeVariant.Dark ? Current.Dark : Current.Light;
}
