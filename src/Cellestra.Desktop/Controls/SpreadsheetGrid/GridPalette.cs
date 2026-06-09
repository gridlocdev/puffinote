using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The colors the custom-drawn grid paints with, resolved for the active light/dark theme so the
/// canvas, headers, gridlines, default text, and table banding stay consistent with the Fluent
/// chrome around them (PRD §6.6). Document cell fills and font colors are data, not theme, and are
/// drawn as-is; only the grid's own chrome and the "automatic" text color follow the theme. Accent
/// colors (selection, active outline) read on both themes and are shared.
/// </summary>
internal sealed class GridPalette
{
    public required IBrush Canvas { get; init; }

    public required ImmutablePen GridLine { get; init; }

    public required IBrush HeaderFill { get; init; }

    public required IBrush ActiveHeaderFill { get; init; }

    public required IBrush HeaderText { get; init; }

    public required IBrush DefaultText { get; init; }

    public required ImmutablePen FreezeDivider { get; init; }

    public required IBrush TableHeaderBand { get; init; }

    public required IBrush TableRowBand { get; init; }

    /// <summary>Background for floating popups (filter / validation dropdowns) so themed list text contrasts.</summary>
    public required IBrush PopupSurface { get; init; }

    public required IBrush SelectionFill { get; init; }

    public required ImmutablePen ActiveOutline { get; init; }

    /// <summary>The colour a cell's hyperlinked text is drawn in — a blue that reads on both themes.</summary>
    public required IBrush Hyperlink { get; init; }

    public static GridPalette For(ThemeVariant variant) => variant == ThemeVariant.Dark ? Dark : Light;

    private static IBrush Brush(uint argb) => new ImmutableSolidColorBrush(Color.FromUInt32(argb));

    private static ImmutablePen Pen(uint argb, double thickness = 1.0) =>
        new(new ImmutableSolidColorBrush(Color.FromUInt32(argb)), thickness);

    private static readonly GridPalette Light = new()
    {
        Canvas = Brush(0xFFFFFFFF),
        GridLine = Pen(0xFFE0E0E0),
        HeaderFill = Brush(0xFFF4F4F4),
        ActiveHeaderFill = Brush(0xFFE6EFFB),
        HeaderText = Brush(0xFF555555),
        DefaultText = Brush(0xFF000000),
        FreezeDivider = Pen(0xFF9A9A9A, 1.5),
        TableHeaderBand = Brush(0xFFD9E5F5),
        TableRowBand = Brush(0xFFF2F7FD),
        PopupSurface = Brush(0xFFFFFFFF),
        SelectionFill = Brush(0x222A7DE1),
        ActiveOutline = Pen(0xFF2A7DE1, 2),
        Hyperlink = Brush(0xFF0B5FCC),
    };

    private static readonly GridPalette Dark = new()
    {
        Canvas = Brush(0xFF1E1E1E),
        GridLine = Pen(0xFF3A3A3A),
        HeaderFill = Brush(0xFF2A2A2A),
        ActiveHeaderFill = Brush(0xFF2F3E52),
        HeaderText = Brush(0xFFB0B0B0),
        DefaultText = Brush(0xFFE6E6E6),
        FreezeDivider = Pen(0xFF5A5A5A, 1.5),
        TableHeaderBand = Brush(0xFF2C3A4D),
        TableRowBand = Brush(0xFF232A33),
        PopupSurface = Brush(0xFF2B2B2B),
        SelectionFill = Brush(0x332A7DE1),
        ActiveOutline = Pen(0xFF4A90E2, 2),
        Hyperlink = Brush(0xFF6BB0FF),
    };
}
