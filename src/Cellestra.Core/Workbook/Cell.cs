using Cellestra.Core.Values;

namespace Cellestra.Core.Model;

/// <summary>
/// One cell's content: either a literal value or a formula, plus a style reference,
/// an optional hyperlink, and the last computed value.
/// <para>
/// For a literal cell, <see cref="Formula"/> is null and <see cref="Value"/> is the content.
/// For a formula cell, <see cref="Formula"/> holds the source (without the leading <c>=</c>)
/// and <see cref="Value"/> holds the cached/computed result — shown immediately on open,
/// then refreshed by the recalculation engine (PRD §8.6).
/// </para>
/// </summary>
public sealed class Cell
{
    /// <summary>The formula source, without the leading <c>=</c> (e.g. <c>SUM(A1:A3)</c>), or null for a literal cell.</summary>
    public string? Formula { get; set; }

    /// <summary>The literal value (literal cell) or the cached computed result (formula cell).</summary>
    public CellValue Value { get; set; } = CellValue.Empty;

    /// <summary>Index into the workbook's style pool. 0 is the default style.</summary>
    public int StyleId { get; set; }

    public Hyperlink? Hyperlink { get; set; }

    public bool IsFormula => Formula is not null;

    /// <summary>True when the cell carries nothing worth storing — used to prune sparse storage.</summary>
    public bool IsBlank =>
        Formula is null && Value.IsEmpty && StyleId == 0 && Hyperlink is null;

    public Cell Clone() => new()
    {
        Formula = Formula,
        Value = Value,
        StyleId = StyleId,
        Hyperlink = Hyperlink,
    };
}
