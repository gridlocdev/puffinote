namespace Cellestra.Core.Model;

/// <summary>
/// A merged-cell region: a rectangle whose top-left "anchor" cell holds the content
/// and spans the rest. Stored per worksheet; mapped to ODF
/// <c>table:number-columns-spanned</c> / <c>table:number-rows-spanned</c> on save.
/// </summary>
public sealed record MergedRegion(CellRange Range)
{
    public CellAddress Anchor => Range.TopLeft;

    public bool Covers(int row, int column) => Range.Contains(row, column);

    /// <summary>True for every covered cell except the anchor (those are visually hidden by the merge).</summary>
    public bool IsCovered(int row, int column) =>
        Covers(row, column) && !(row == Range.Top && column == Range.Left);
}
