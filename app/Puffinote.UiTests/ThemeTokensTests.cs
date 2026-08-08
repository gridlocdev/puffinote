using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Puffinote.Components.Theming;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The chrome tokens under a theme change. What has to hold is that the switch reaches chrome that was
/// built long before it: every panel, row and badge in the family captured its brush at construction, and
/// none of them is rebuilt when someone picks Light in Preferences. That works only because
/// <see cref="Tokens.Active"/> hands out one fixed set of brushes and re-tints those in place, so the
/// brush a control is already painting with becomes the light one.
/// <para>
/// Headless-UI tests rather than plain ones because re-tinting writes to Avalonia brushes, which have
/// thread affinity — which is no constraint on the app (a theme change always arrives on the UI thread,
/// from a dialog or from the OS) but does decide which test assembly this belongs in.
/// </para>
/// </summary>
public sealed class ThemeTokensTests : IDisposable
{
    private readonly ThemeVariant _original = Tokens.Variant;

    public void Dispose() => Tokens.Variant = _original;

    private static Color ColorOf(IBrush brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void Switching_variant_retints_the_very_brushes_chrome_is_already_painting_with()
    {
        Tokens.Variant = ThemeVariant.Dark;
        var surface = Tokens.Active.Surface; // what a panel captured when it was built
        var text = Tokens.Active.Text;
        var darkSurface = ColorOf(surface);

        Tokens.Variant = ThemeVariant.Light;

        Assert.Same(surface, Tokens.Active.Surface); // same instance — nothing had to be rebuilt…
        Assert.NotEqual(darkSurface, ColorOf(surface)); // …and it is now painting the light surface
        Assert.Equal(ColorOf(Tokens.Light.Surface), ColorOf(surface));
        Assert.Equal(ColorOf(Tokens.Light.Text), ColorOf(text));
    }

    [AvaloniaFact]
    public void The_variant_a_theme_resolves_to_is_the_palette_it_paints()
    {
        Tokens.Variant = ThemeVariant.Light;
        Assert.Equal(ColorOf(Tokens.Light.Surface), ColorOf(Tokens.Active.Surface));

        Tokens.Variant = ThemeVariant.Dark;
        Assert.Equal(ColorOf(Tokens.Dark.Surface), ColorOf(Tokens.Active.Surface));
    }

    [AvaloniaFact]
    public void System_is_the_default_and_hands_the_choice_to_the_OS()
    {
        // ThemeVariant.Default is Avalonia's "whatever the OS says", which is what makes an app left on
        // System follow a desktop that switches itself — rather than a light/dark we resolved once at launch.
        Assert.Equal(ThemeVariant.Default, ThemePreference.System.ToVariant());
        Assert.Equal(ThemeVariant.Light, ThemePreference.Light.ToVariant());
        Assert.Equal(ThemeVariant.Dark, ThemePreference.Dark.ToVariant());

        // The drop-down's labels are in declaration order, which is the order an index maps back to a value.
        Assert.Equal(["Follow System", "Light", "Dark"], ThemePreferences.Labels);
    }
}
