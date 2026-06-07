using Cellestra.Core.Data;
using Cellestra.Core.Values;

namespace Cellestra.Core.Model;

/// <summary>
/// One worksheet: a sparse grid of cells plus per-row/column layout, merged regions,
/// and frozen-pane state. Cells are stored only when non-blank (ARCHITECTURE "no object
/// per cell"); the UI grid reads positions directly from here and draws only what's visible.
/// </summary>
public sealed class Worksheet
{
    private readonly Dictionary<(int Row, int Column), Cell> _cells = new();
    private readonly Dictionary<int, Row> _rows = new();
    private readonly Dictionary<int, Column> _columns = new();
    private readonly List<MergedRegion> _mergedRegions = new();
    private readonly List<Table> _tables = new();
    private readonly List<DataValidation> _validations = new();

    public Worksheet(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; set; }

    /// <summary>Number of rows frozen at the top (0 = none).</summary>
    public int FrozenRows { get; set; }

    /// <summary>Number of columns frozen at the left (0 = none).</summary>
    public int FrozenColumns { get; set; }

    public IReadOnlyList<MergedRegion> MergedRegions => _mergedRegions;

    public int CellCount => _cells.Count;

    // ---- Cells ------------------------------------------------------------

    /// <summary>The cell at this position, or null when blank.</summary>
    public Cell? Find(int row, int column) =>
        _cells.TryGetValue((row, column), out var cell) ? cell : null;

    public Cell? Find(CellAddress address) => Find(address.Row, address.Column);

    /// <summary>The displayed value at this position; <see cref="CellValue.Empty"/> when blank.</summary>
    public CellValue GetValue(int row, int column) => Find(row, column)?.Value ?? CellValue.Empty;

    public CellValue GetValue(CellAddress address) => GetValue(address.Row, address.Column);

    /// <summary>Get the cell, creating and storing a blank one if it does not exist yet.</summary>
    public Cell GetOrCreate(int row, int column)
    {
        ValidatePosition(row, column);
        if (!_cells.TryGetValue((row, column), out var cell))
        {
            cell = new Cell();
            _cells[(row, column)] = cell;
        }

        return cell;
    }

    public Cell GetOrCreate(CellAddress address) => GetOrCreate(address.Row, address.Column);

    /// <summary>Set a literal value, clearing any formula. Prunes the cell if it becomes blank.</summary>
    public void SetValue(int row, int column, CellValue value)
    {
        if (value.IsEmpty)
        {
            // Clearing content: keep style/hyperlink if present, else drop the cell.
            var existing = Find(row, column);
            if (existing is null)
            {
                return;
            }

            existing.Formula = null;
            existing.Value = CellValue.Empty;
            PruneIfBlank(row, column, existing);
            return;
        }

        var cell = GetOrCreate(row, column);
        cell.Formula = null;
        cell.Value = value;
    }

    /// <summary>Set a formula (source without the leading <c>=</c>) and seed its cached value.</summary>
    public void SetFormula(int row, int column, string formula, CellValue cachedValue)
    {
        var cell = GetOrCreate(row, column);
        cell.Formula = formula;
        cell.Value = cachedValue;
    }

    /// <summary>Remove a cell entirely (content, style, hyperlink).</summary>
    public void Remove(int row, int column) => _cells.Remove((row, column));

    /// <summary>Place (or, when null/blank, clear) a cell at a position — used to restore undo state.</summary>
    public void PutCell(int row, int column, Cell? cell)
    {
        if (cell is null || cell.IsBlank)
        {
            _cells.Remove((row, column));
        }
        else
        {
            _cells[(row, column)] = cell;
        }
    }

    /// <summary>Drop the cell from storage if nothing distinguishes it from blank.</summary>
    public void PruneIfBlank(int row, int column, Cell cell)
    {
        if (cell.IsBlank)
        {
            _cells.Remove((row, column));
        }
    }

    /// <summary>All non-blank cells with their positions, in no particular order.</summary>
    public IEnumerable<(int Row, int Column, Cell Cell)> EnumerateCells()
    {
        foreach (var ((row, column), cell) in _cells)
        {
            yield return (row, column, cell);
        }
    }

    // ---- Rows & columns ---------------------------------------------------

    public Row? FindRow(int row) => _rows.TryGetValue(row, out var r) ? r : null;

    public Column? FindColumn(int column) => _columns.TryGetValue(column, out var c) ? c : null;

    public Row GetOrCreateRow(int row)
    {
        if (!_rows.TryGetValue(row, out var r))
        {
            r = new Row();
            _rows[row] = r;
        }

        return r;
    }

    public Column GetOrCreateColumn(int column)
    {
        if (!_columns.TryGetValue(column, out var c))
        {
            c = new Column();
            _columns[column] = c;
        }

        return c;
    }

    public double GetRowHeight(int row) => FindRow(row)?.Height ?? Defaults.RowHeight;

    public double GetColumnWidth(int column) => FindColumn(column)?.Width ?? Defaults.ColumnWidth;

    public bool IsRowHidden(int row) => FindRow(row)?.Hidden ?? false;

    public bool IsColumnHidden(int column) => FindColumn(column)?.Hidden ?? false;

    public IReadOnlyDictionary<int, Row> Rows => _rows;

    public IReadOnlyDictionary<int, Column> Columns => _columns;

    // ---- Merged regions ---------------------------------------------------

    public MergedRegion? FindMerge(int row, int column)
    {
        foreach (var region in _mergedRegions)
        {
            if (region.Covers(row, column))
            {
                return region;
            }
        }

        return null;
    }

    public void AddMerge(MergedRegion region)
    {
        if (_mergedRegions.Any(existing => existing.Range.Intersects(region.Range)))
        {
            throw new InvalidOperationException("Merged regions may not overlap (PRD §8.10).");
        }

        _mergedRegions.Add(region);
    }

    public bool RemoveMerge(int row, int column)
    {
        var region = FindMerge(row, column);
        return region is not null && _mergedRegions.Remove(region);
    }

    // ---- Tables & validation ----------------------------------------------

    public IReadOnlyList<Table> Tables => _tables;

    public IReadOnlyList<DataValidation> Validations => _validations;

    public void AddTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (_tables.Any(t => t.Range.Intersects(table.Range)))
        {
            throw new InvalidOperationException("Tables may not overlap (PRD §8.10).");
        }

        _tables.Add(table);
    }

    public bool RemoveTable(Table table) => _tables.Remove(table);

    public void ReplaceTable(Table existing, Table replacement)
    {
        int index = _tables.IndexOf(existing);
        if (index >= 0)
        {
            _tables[index] = replacement;
        }
    }

    public Table? FindTable(int row, int column) => _tables.FirstOrDefault(t => t.Range.Contains(row, column));

    public void AddValidation(DataValidation validation) => _validations.Add(validation);

    public bool RemoveValidation(DataValidation validation) => _validations.Remove(validation);

    public DataValidation? FindValidation(int row, int column) =>
        _validations.FirstOrDefault(v => v.Covers(row, column));

    // ---- Extents ----------------------------------------------------------

    /// <summary>
    /// The bounding rectangle of all used content (cells, sized rows/columns, merges),
    /// or null when the sheet is completely empty.
    /// </summary>
    public CellRange? GetUsedRange()
    {
        // Track the two axes independently: a sized-but-empty column extends the
        // column span without dragging the row span down to row 0, and vice versa.
        bool hasRow = false, hasColumn = false;
        int top = int.MaxValue, bottom = 0, left = int.MaxValue, right = 0;

        void ExtendRows(int from, int to)
        {
            hasRow = true;
            top = Math.Min(top, from);
            bottom = Math.Max(bottom, to);
        }

        void ExtendColumns(int from, int to)
        {
            hasColumn = true;
            left = Math.Min(left, from);
            right = Math.Max(right, to);
        }

        foreach (var ((row, column), _) in _cells)
        {
            ExtendRows(row, row);
            ExtendColumns(column, column);
        }

        foreach (var region in _mergedRegions)
        {
            ExtendRows(region.Range.Top, region.Range.Bottom);
            ExtendColumns(region.Range.Left, region.Range.Right);
        }

        foreach (int row in _rows.Keys)
        {
            ExtendRows(row, row);
        }

        foreach (int column in _columns.Keys)
        {
            ExtendColumns(column, column);
        }

        if (!hasRow && !hasColumn)
        {
            return null;
        }

        // If only one axis has content, the other collapses to index 0.
        if (!hasRow)
        {
            top = bottom = 0;
        }

        if (!hasColumn)
        {
            left = right = 0;
        }

        return new CellRange(new CellAddress(left, top), new CellAddress(right, bottom));
    }

    // ---- State capture (for structural undo) -----------------------------

    /// <summary>A deep snapshot of a worksheet's grid state, for undoing structural edits.</summary>
    internal sealed class State
    {
        internal required Dictionary<(int, int), Cell> Cells { get; init; }
        internal required Dictionary<int, Row> Rows { get; init; }
        internal required Dictionary<int, Column> Columns { get; init; }
        internal required List<MergedRegion> Merges { get; init; }
        internal required int FrozenRows { get; init; }
        internal required int FrozenColumns { get; init; }
    }

    internal State Capture() => new()
    {
        Cells = _cells.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        Rows = _rows.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        Columns = _columns.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        Merges = new List<MergedRegion>(_mergedRegions),
        FrozenRows = FrozenRows,
        FrozenColumns = FrozenColumns,
    };

    internal void Restore(State state)
    {
        _cells.Clear();
        foreach (var (key, cell) in state.Cells)
        {
            _cells[key] = cell.Clone();
        }

        _rows.Clear();
        foreach (var (key, row) in state.Rows)
        {
            _rows[key] = row.Clone();
        }

        _columns.Clear();
        foreach (var (key, column) in state.Columns)
        {
            _columns[key] = column.Clone();
        }

        _mergedRegions.Clear();
        _mergedRegions.AddRange(state.Merges);
        FrozenRows = state.FrozenRows;
        FrozenColumns = state.FrozenColumns;
    }

    private static void ValidatePosition(int row, int column)
    {
        if (!SheetDimensions.IsValidRow(row))
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "Row index out of range.");
        }

        if (!SheetDimensions.IsValidColumn(column))
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Column index out of range.");
        }
    }
}
