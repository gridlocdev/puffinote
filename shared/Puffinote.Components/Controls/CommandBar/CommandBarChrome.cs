using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Puffinote.Components.Controls.CommandBar;

/// <summary>
/// Shared visual language for the family's adaptive command bars (the spreadsheet's and the note editor's):
/// the accent/hover/divider brushes and the small pointer-over highlight helper, so both bars look and feel
/// identical without either owning the palette. Theme-neutral ARGB washes that read on light and dark chrome.
/// </summary>
public static class CommandBarChrome
{
    /// <summary>Emerald wash behind an "on" toggle tool — legible on both light and dark chrome.</summary>
    public static readonly IBrush ActiveBackground = new SolidColorBrush(Color.FromArgb(0x33, 0x15, 0x73, 0x4F));

    /// <summary>Emerald foreground for an "on" toggle tool's icon/dot.</summary>
    public static readonly IBrush ActiveForeground = new SolidColorBrush(Color.FromRgb(0x1A, 0x8A, 0x5E));

    /// <summary>Subtle grey pointer-over wash — lighter than <see cref="ActiveBackground"/> so an on-and-hovered tool still reads as on.</summary>
    public static readonly IBrush HoverBackground = new SolidColorBrush(Color.FromArgb(0x1A, 0x88, 0x88, 0x88));

    /// <summary>Hairline divider between tool groups.</summary>
    public static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88));

    /// <summary>Muted grey for chevrons, secondary glyphs, and hint text.</summary>
    public static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    /// <summary>
    /// Give a transparent-backed control a brief, subtle pointer-over highlight. On exit it returns to
    /// <paramref name="rest"/> (Transparent by default), so callers that carry their own resting wash pass it
    /// to avoid clobbering it.
    /// </summary>
    public static void AddHover(TemplatedControl control, IBrush? rest = null)
    {
        rest ??= Brushes.Transparent;
        control.PointerEntered += (_, _) => control.Background = HoverBackground;
        control.PointerExited += (_, _) => control.Background = rest;
    }
}
