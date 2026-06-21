namespace Cellestra.Core.Dependencies;

/// <summary>
/// Records which cells each formula reads (its precedents) and the reverse (its dependents),
/// so the recalculation engine can recompute only what an edit affects, in the right order.
/// Stores edges at cell granularity; ranges were expanded by <see cref="ReferenceCollector"/>.
/// </summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<CellRef, HashSet<CellRef>> _precedents = new();
    private readonly Dictionary<CellRef, HashSet<CellRef>> _dependents = new();

    private static readonly IReadOnlyCollection<CellRef> Empty = Array.Empty<CellRef>();

    /// <summary>Replace the set of cells that <paramref name="cell"/> reads.</summary>
    public void SetPrecedents(CellRef cell, IReadOnlyCollection<CellRef> precedents)
    {
        ClearPrecedents(cell);
        if (precedents.Count == 0)
        {
            return;
        }

        // Keep a self-edge when a formula reads its own cell (directly, e.g. =A1+1, or via a range
        // that covers it, e.g. =SUM(A1:A5)). It gives the cell in-degree 1 in the topological sort,
        // so it is reported as circular instead of being silently evaluated once against its own
        // stale value. The affected-set walk and the sort both guard with visited sets, so the
        // self-edge cannot loop.
        var set = new HashSet<CellRef>(precedents);
        _precedents[cell] = set;
        foreach (var precedent in set)
        {
            if (!_dependents.TryGetValue(precedent, out var dependents))
            {
                dependents = new HashSet<CellRef>();
                _dependents[precedent] = dependents;
            }

            dependents.Add(cell);
        }
    }

    /// <summary>Drop a cell's outgoing edges (when it stops being a formula). Incoming edges remain.</summary>
    public void RemoveFormula(CellRef cell) => ClearPrecedents(cell);

    public IReadOnlyCollection<CellRef> GetPrecedents(CellRef cell) =>
        _precedents.TryGetValue(cell, out var set) ? set : Empty;

    public IReadOnlyCollection<CellRef> GetDependents(CellRef cell) =>
        _dependents.TryGetValue(cell, out var set) ? set : Empty;

    public void Clear()
    {
        _precedents.Clear();
        _dependents.Clear();
    }

    private void ClearPrecedents(CellRef cell)
    {
        if (!_precedents.TryGetValue(cell, out var old))
        {
            return;
        }

        foreach (var precedent in old)
        {
            if (_dependents.TryGetValue(precedent, out var dependents))
            {
                dependents.Remove(cell);
                if (dependents.Count == 0)
                {
                    _dependents.Remove(precedent);
                }
            }
        }

        _precedents.Remove(cell);
    }
}
