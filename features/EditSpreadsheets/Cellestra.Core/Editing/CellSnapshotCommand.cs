using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// A non-structural edit that mutates a known set of cell positions: it snapshots those cells
/// before applying a mutation and restores them on undo. Backs set-value, clear, fill, paste,
/// style, sort, remove-duplicates, and text-to-columns — anything that rewrites cells in place.
/// </summary>
public sealed class CellSnapshotCommand : IEditCommand
{
    private readonly string _sheetName;
    private IReadOnlyList<(int Row, int Column)> _positions;
    private readonly Func<Worksheet, IReadOnlyList<(int Row, int Column)>>? _positionFactory;
    private readonly Action<Worksheet> _mutate;
    private List<(int Row, int Column, Cell? Before)>? _snapshot;

    public CellSnapshotCommand(string label, string sheetName, IReadOnlyList<(int Row, int Column)> positions, Action<Worksheet> mutate)
    {
        Label = label;
        _sheetName = sheetName;
        _positions = positions;
        _mutate = mutate;
    }

    /// <summary>
    /// As the eager overload, but the affected positions are computed from the worksheet at
    /// <see cref="Apply"/> time. Lets range-iterating edits clamp to the used range instead of
    /// materializing a whole-sheet selection (16384×1048576 ≈ 17 billion cells), which otherwise
    /// hangs/crashes the app when an operation runs over an "everything" selection (PRD §10.1).
    /// </summary>
    public CellSnapshotCommand(string label, string sheetName, Func<Worksheet, IReadOnlyList<(int Row, int Column)>> positionFactory, Action<Worksheet> mutate)
    {
        Label = label;
        _sheetName = sheetName;
        _positions = Array.Empty<(int, int)>();
        _positionFactory = positionFactory;
        _mutate = mutate;
    }

    public string Label { get; }

    public EditResult Apply(Workbook workbook)
    {
        var sheet = workbook.FindSheet(_sheetName) ?? throw new InvalidOperationException($"Sheet '{_sheetName}' not found.");

        if (_positionFactory is not null)
        {
            _positions = _positionFactory(sheet);
        }

        _snapshot = new List<(int, int, Cell?)>(_positions.Count);
        foreach (var (row, column) in _positions)
        {
            _snapshot.Add((row, column, sheet.Find(row, column)?.Clone()));
        }

        _mutate(sheet);
        return EditResult.Cells(ToRefs());
    }

    public EditResult Undo(Workbook workbook)
    {
        var sheet = workbook.FindSheet(_sheetName) ?? throw new InvalidOperationException($"Sheet '{_sheetName}' not found.");
        if (_snapshot is null)
        {
            return EditResult.None;
        }

        foreach (var (row, column, before) in _snapshot)
        {
            sheet.PutCell(row, column, before?.Clone());
        }

        return EditResult.Cells(ToRefs());
    }

    private List<CellRef> ToRefs()
    {
        var refs = new List<CellRef>(_positions.Count);
        foreach (var (row, column) in _positions)
        {
            refs.Add(new CellRef(_sheetName, row, column));
        }

        return refs;
    }
}
