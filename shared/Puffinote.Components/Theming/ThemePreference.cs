using Avalonia;
using Avalonia.Styling;

namespace Puffinote.Components.Theming;

/// <summary>
/// How an app decides whether it is light or dark — the one appearance preference every app in the
/// family offers, in Preferences ▸ Appearance ▸ Theme.
/// <para>
/// <see cref="System"/> is the default, and is not a third colour scheme: it hands the choice to the OS
/// and keeps handing it over, so an app left on it follows a Mac that switches itself at dusk. Choosing
/// <see cref="Light"/> or <see cref="Dark"/> takes the choice back for good — that is the point of
/// picking one — and it survives the session, because someone who needs a light screen needs it every
/// morning, not once.
/// </para>
/// </summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Turning a <see cref="ThemePreference"/> into what the UI does about it: the labels the Preferences
/// drop-down shows, and applying the choice to a running app. Shared so the five apps offer the same
/// three options, worded the same way, and mean exactly the same thing by each.
/// </summary>
public static class ThemePreferences
{
    /// <summary>
    /// The drop-down's options, in <see cref="ThemePreference"/> declaration order — which is the order
    /// <c>ChoiceSetting.ForEnum</c> maps an index back to a value in.
    /// </summary>
    public static IReadOnlyList<string> Labels { get; } = [Label(ThemePreference.System), "Light", "Dark"];

    /// <summary>The one-line explanation under the Theme row. Says what "System" costs you: nothing.</summary>
    public const string Hint = "System follows your OS appearance, and keeps following it when it changes.";

    /// <summary>The label for one preference (the menu item's text as much as the drop-down's).</summary>
    public static string Label(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => "Light",
        ThemePreference.Dark => "Dark",
        _ => "Follow System",
    };

    /// <summary>
    /// The Avalonia variant to request. <see cref="ThemeVariant.Default"/> is how Avalonia spells
    /// "whatever the OS says", so <see cref="ThemePreference.System"/> maps to it rather than to a
    /// resolved light/dark we would then have to keep re-resolving ourselves.
    /// </summary>
    public static ThemeVariant ToVariant(this ThemePreference theme) => theme switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>
    /// Apply <paramref name="theme"/> to <paramref name="app"/> and return the variant it resolved to —
    /// the app's own for an explicit choice, the OS's for <see cref="ThemePreference.System"/>. The
    /// chrome tokens are re-tinted as part of this, so a caller only has to hand on the returned variant
    /// to whatever else it themes by hand (an editor's own palette, say).
    /// <para>
    /// Setting <c>RequestedThemeVariant</c> is what re-resolves every <c>ThemeDictionaries</c> lookup and
    /// every control template, so Fluent's own chrome switches with it; <see cref="Tokens"/> covers the
    /// family's chrome, which paints from tokens rather than from control themes.
    /// </para>
    /// </summary>
    public static ThemeVariant Apply(this ThemePreference theme, Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.RequestedThemeVariant = theme.ToVariant();
        Tokens.Variant = app.ActualThemeVariant;
        return app.ActualThemeVariant;
    }
}
