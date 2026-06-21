namespace Cellestra.Core.Styling;

/// <summary>
/// The complete visual style of a cell: font, fill, borders, alignment, and number format.
/// An immutable value (record) so the <see cref="StylePool"/> can store each distinct style
/// once and hand out an index — mirroring how <c>.ods</c> uses named styles.
/// </summary>
public sealed record CellStyle(
    Font Font,
    Fill Fill,
    Border Border,
    Alignment Alignment,
    NumberFormat NumberFormat)
{
    /// <summary>The default cell style (PRD §8.8). Lives at index 0 in every <see cref="StylePool"/>.</summary>
    public static readonly CellStyle Default = new(
        Font.Default,
        Fill.None,
        Border.None,
        Alignment.Default,
        NumberFormat.General);

    public CellStyle WithFont(Font font) => this with { Font = font };

    public CellStyle WithFill(Fill fill) => this with { Fill = fill };

    public CellStyle WithBorder(Border border) => this with { Border = border };

    public CellStyle WithAlignment(Alignment alignment) => this with { Alignment = alignment };

    public CellStyle WithNumberFormat(NumberFormat numberFormat) => this with { NumberFormat = numberFormat };
}
