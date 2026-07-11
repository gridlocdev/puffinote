using Avalonia.Media;

namespace Puffinote.Components.Theming;

/// <summary>
/// The semantic design tokens the Puffinote family's shell chrome (palette, modals, tooltips,
/// selection) reads, for one theme variant. Apps supply concrete values via an <see cref="IAppTheme"/>;
/// the shared primitives never hardcode colors. Domain-surface styling (grid cells, Markdown syntax)
/// is app-specific and intentionally not modelled here.
/// </summary>
/// <param name="Accent">The app's primary accent (selection, active state, focus).</param>
/// <param name="AccentSubtle">A low-alpha accent wash for selected rows / highlights.</param>
/// <param name="Surface">A floating card / panel background (palette box, modal card).</param>
/// <param name="SurfaceHover">The wash under the pointer on a row or an icon button — one step off
/// <see cref="Surface"/>, well short of <see cref="SurfaceBorder"/>. Hover was being painted with
/// <see cref="SurfaceBorder"/> for want of a token, which is a border weight, far too loud as a fill.</param>
/// <param name="SurfaceInset">The well of a small element recessed <em>into</em> a <see cref="Surface"/> — a
/// badge, a type pill, a keycap hint. Recessed rather than raised on purpose: an opaque fill darker than the
/// surface means the badge's text keeps its contrast ratio no matter what the row behind it is doing (hovered,
/// selected, accent-washed), which a translucent or lighter chip cannot promise.</param>
/// <param name="SurfaceBorder">The hairline border around a <see cref="Surface"/>.</param>
/// <param name="MutedText">Secondary / description text.</param>
/// <param name="Scrim">The dim backdrop behind a modal overlay.</param>
public sealed record ThemePalette(
    IBrush Accent,
    IBrush AccentSubtle,
    IBrush Surface,
    IBrush SurfaceHover,
    IBrush SurfaceInset,
    IBrush SurfaceBorder,
    IBrush MutedText,
    IBrush Scrim);
