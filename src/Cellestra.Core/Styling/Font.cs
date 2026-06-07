namespace Cellestra.Core.Styling;

/// <summary>A cell's typeface settings. Immutable so styles de-duplicate by value.</summary>
public sealed record Font(
    string Family,
    double SizePoints,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    ArgbColor Color = default)
{
    /// <summary>The default font (PRD §8.8): Helvetica Neue, 10 pt, black, no emphasis.</summary>
    public static readonly Font Default = new(Defaults.FontFamily, Defaults.FontSizePoints, Color: ArgbColor.Black);

    public Font WithBold(bool bold) => this with { Bold = bold };

    public Font WithItalic(bool italic) => this with { Italic = italic };

    public Font WithUnderline(bool underline) => this with { Underline = underline };
}
