namespace Cellestra.Core.Dependencies;

/// <summary>
/// A fully-qualified cell identity (sheet + position) used as a node key in the dependency
/// graph. Unlike <c>CellAddress</c> it carries no <c>$</c> flags — it is pure identity.
/// Sheet names compare case-insensitively, matching workbook lookup.
/// </summary>
public readonly record struct CellRef(string Sheet, int Row, int Column)
{
    public bool Equals(CellRef other) =>
        Row == other.Row && Column == other.Column &&
        string.Equals(Sheet, other.Sheet, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Sheet), Row, Column);

    public override string ToString() => $"{Sheet}!R{Row}C{Column}";
}
