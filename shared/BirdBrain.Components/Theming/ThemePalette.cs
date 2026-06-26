using Avalonia.Media;

namespace BirdBrain.Components.Theming;

/// <summary>
/// The semantic design tokens the BirdBrain family's shell chrome (palette, modals, tooltips,
/// selection) reads, for one theme variant. Apps supply concrete values via an <see cref="IAppTheme"/>;
/// the shared primitives never hardcode colors. Domain-surface styling (grid cells, Markdown syntax)
/// is app-specific and intentionally not modelled here.
/// </summary>
/// <param name="Accent">The app's primary accent (selection, active state, focus).</param>
/// <param name="AccentSubtle">A low-alpha accent wash for selected rows / highlights.</param>
/// <param name="Surface">A floating card / panel background (palette box, modal card).</param>
/// <param name="SurfaceBorder">The hairline border around a <see cref="Surface"/>.</param>
/// <param name="MutedText">Secondary / description text.</param>
/// <param name="Scrim">The dim backdrop behind a modal overlay.</param>
public sealed record ThemePalette(
    IBrush Accent,
    IBrush AccentSubtle,
    IBrush Surface,
    IBrush SurfaceBorder,
    IBrush MutedText,
    IBrush Scrim);
