namespace Cellestra.Core.Dependencies;

/// <summary>
/// Raised internally when the recalculation engine finds a cycle. The engine catches it and
/// marks the cells in the cycle with a #REF! "Circular reference" error rather than letting
/// it surface (ARCHITECTURE §5: circular references are detected and reported).
/// </summary>
public sealed class CircularReferenceException : Exception
{
    public CircularReferenceException(IReadOnlyCollection<CellRef> cells)
        : base("A circular reference was detected.")
    {
        Cells = cells;
    }

    public IReadOnlyCollection<CellRef> Cells { get; }
}
