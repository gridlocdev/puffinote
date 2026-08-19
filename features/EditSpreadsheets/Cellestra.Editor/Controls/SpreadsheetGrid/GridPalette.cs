using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The colors the custom-drawn grid paints with, resolved for the active light/dark theme so the
/// canvas, headers, gridlines, default text, and table banding stay consistent with the Fluent
/// chrome around them (PRD §6.6). Document cell fills and font colors are data, not theme, and are
/// drawn as-is; only the grid's own chrome and the "automatic" text color follow the theme — and the
/// automatic color follows what is actually <i>behind</i> it, not the theme alone, so a cell the
/// document filled light keeps dark ink in dark mode. Accent colors (selection, active outline) read
/// on both themes and are shared.
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

    /// <summary>
    /// The band behind a table's header row. This — not a baked cell fill — <em>is</em> the header's
    /// look: header colour is theme chrome, so a table reads as a table in light and dark mode alike,
    /// and its outline (<see cref="SpreadsheetGrid"/>'s table border) is drawn in the same colour. A
    /// fill the user applies themselves is document data and draws over it.
    /// </summary>
    public required IBrush TableHeaderBand { get; init; }

    /// <summary>
    /// The band behind a table's header <em>column</em> — a step quieter than
    /// <see cref="TableHeaderBand"/>, so the column titles outrank the row labels and the corner cell
    /// (which belongs to the row) stays unambiguous. Chrome for the same reason the header band is.
    /// </summary>
    public required IBrush TableHeaderColumnBand { get; init; }

    /// <summary>
    /// The stripe on a banded table's alternate data rows: the header band's colour at low opacity, so
    /// banding always reads as a muted echo of the header rather than a colour of its own. Drawn only
    /// when the table opted in (<c>Table.HasBandedRows</c> — off by default).
    /// </summary>
    public required IBrush TableRowBand { get; init; }

    /// <summary>Background for floating popups (filter / validation dropdowns) so themed list text contrasts.</summary>
    public required IBrush PopupSurface { get; init; }

    public required IBrush SelectionFill { get; init; }

    public required ImmutablePen ActiveOutline { get; init; }

    /// <summary>The colour a cell's hyperlinked text is drawn in — a blue that reads on this theme's canvas.</summary>
    public required IBrush Hyperlink { get; init; }

    /// <summary>
    /// The other theme's link blue, used when a link sits on a cell fill this theme's blue can't be read
    /// on — a light-blue link on a light grey header, say. Swapping to the opposite blue keeps the link
    /// reading as a link, where the generic contrast rescue would have to fall back to plain body ink.
    /// </summary>
    public required IBrush HyperlinkAlternate { get; init; }

    /// <summary>The red double-chevron flag drawn at the clipped edge of text too wide for its cell (à la Calc).</summary>
    public required IBrush OverflowMarker { get; init; }

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
        TableHeaderBand = Brush(0xFFC6C6C6),
        TableHeaderColumnBand = Brush(0xFFDCDCDC),
        TableRowBand = Brush(0x40C6C6C6),
        PopupSurface = Brush(0xFFFFFFFF),
        SelectionFill = Brush(0x222A7DE1),
        ActiveOutline = Pen(0xFF2A7DE1, 2),
        Hyperlink = Brush(0xFF0B5FCC),
        HyperlinkAlternate = Brush(0xFF6BB0FF),
        OverflowMarker = Brush(0xFFE53935),
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
        TableHeaderBand = Brush(0xFF3A3A3C),
        TableHeaderColumnBand = Brush(0xFF2C2C2E),
        TableRowBand = Brush(0x663A3A3C),
        PopupSurface = Brush(0xFF2B2B2B),
        SelectionFill = Brush(0x332A7DE1),
        ActiveOutline = Pen(0xFF4A90E2, 2),
        Hyperlink = Brush(0xFF6BB0FF),
        HyperlinkAlternate = Brush(0xFF0B5FCC),
        OverflowMarker = Brush(0xFFFF5252),
    };
}
