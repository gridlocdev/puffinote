using System.Globalization;

namespace Cellestra.Core.Model;

/// <summary>How formulas recalculate (PRD §8.6). Automatic is the default.</summary>
public enum CalculationMode
{
    Automatic,
    Manual,
}

/// <summary>
/// Document-wide settings: locale (drives number/date/currency formatting per PRD §8.3),
/// calculation mode, and the active sheet. Persisted in the <c>.ods</c> <c>settings.xml</c>.
/// </summary>
public sealed class DocumentSettings
{
    /// <summary>
    /// The document locale. Null means "unspecified" — the app falls back to the system
    /// locale for a new document or any format the file leaves open (PRD §8.3).
    /// </summary>
    public CultureInfo? Locale { get; set; }

    public CalculationMode CalculationMode { get; set; } = CalculationMode.Automatic;

    /// <summary>Zero-based index of the active worksheet.</summary>
    public int ActiveSheetIndex { get; set; }

    /// <summary>The effective locale: the document locale if set, else the current system locale.</summary>
    public CultureInfo EffectiveLocale => Locale ?? CultureInfo.CurrentCulture;
}
