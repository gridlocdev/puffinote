namespace Cellestra.Core.Model;

/// <summary>
/// A named range (e.g. <c>TaxRate</c>) that formulas can reference in place of a literal
/// range (PRD §4 Formulas). Workbook-scoped names live on the <see cref="Workbook"/>;
/// sheet-scoped names set <see cref="Scope"/> to the owning sheet's name.
/// </summary>
public sealed record NamedRange(string Name, CellRange Target)
{
    /// <summary>Null for a workbook-global name; otherwise the sheet the name is local to.</summary>
    public string? Scope { get; init; }

    public bool IsGlobal => Scope is null;
}
