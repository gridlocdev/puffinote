namespace Cellestra.Core.Styling;

/// <summary>The broad category of a number format, used for ribbon toggles and ODF data-style mapping.</summary>
public enum NumberFormatKind
{
    General,
    Number,
    Percent,
    Currency,
    Scientific,
    Fraction,
    Date,
    Time,
    DateTime,
    Text,
}

/// <summary>
/// A cell's number format, held as an Excel/ODF-style code string (the canonical internal
/// form). <see cref="Kind"/> classifies it for the UI; <c>NumberFormatter</c> applies it for
/// display. "General" is the default (PRD §8.8).
/// </summary>
public sealed record NumberFormat(string Code)
{
    public static readonly NumberFormat General = new("General");
    public static readonly NumberFormat Integer = new("0");
    public static readonly NumberFormat TwoDecimals = new("0.00");
    public static readonly NumberFormat Percent = new("0%");
    public static readonly NumberFormat Text = new("@");

    public bool IsGeneral => string.IsNullOrEmpty(Code) ||
        string.Equals(Code, "General", StringComparison.OrdinalIgnoreCase);

    public NumberFormatKind Kind => NumberFormatParser.Classify(Code);
}
