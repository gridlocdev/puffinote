using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The current cell selection: an active cell plus a rectangular range (anchored when the user
/// extends with Shift), optionally with additional disjoint ranges added by Cmd+click (#27/#28).
/// The active cell lives in the most recent range. Pure UI state — never undoable (PRD §8.5).
/// </summary>
public sealed class GridSelection
{
    private int _anchorRow;
    private int _anchorColumn;

    // Ranges committed by earlier Cmd+clicks; the live anchor/active rectangle is appended to these.
    private readonly List<CellRange> _committed = [];

    public int ActiveRow { get; private set; }

    public int ActiveColumn { get; private set; }

    public int Top => Math.Min(_anchorRow, ActiveRow);

    public int Bottom => Math.Max(_anchorRow, ActiveRow);

    public int Left => Math.Min(_anchorColumn, ActiveColumn);

    public int Right => Math.Max(_anchorColumn, ActiveColumn);

    public bool IsSingleCell => Top == Bottom && Left == Right;

    public event EventHandler? Changed;

    public CellAddress ActiveAddress => new(ActiveColumn, ActiveRow);

    public CellRange Range => new(new CellAddress(Left, Top), new CellAddress(Right, Bottom));

    /// <summary>Every selected rectangle, the committed ones followed by the live one.</summary>
    public IReadOnlyList<CellRange> Ranges
    {
        get
        {
            var all = new List<CellRange>(_committed) { Range };
            return all;
        }
    }

    /// <summary>True when the selection is made of more than one disjoint rectangle.</summary>
    public bool IsDiscontiguous => _committed.Count > 0;

    /// <summary>Move the active cell and collapse the selection to it (clears disjoint ranges).</summary>
    public void MoveTo(int row, int column)
    {
        _committed.Clear();
        ActiveRow = _anchorRow = ClampRow(row);
        ActiveColumn = _anchorColumn = ClampColumn(column);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Esc: collapse the selection back to a single cell at the position it was anchored from — the
    /// cell the selection started growing from — and drop any disjoint Cmd+click ranges.
    /// </summary>
    public void CollapseToOrigin() => MoveTo(_anchorRow, _anchorColumn);

    /// <summary>Cmd+click a cell: commit the current rectangle and start a fresh one (#27).</summary>
    public void AddPoint(int row, int column)
    {
        _committed.Add(Range);
        ActiveRow = _anchorRow = ClampRow(row);
        ActiveColumn = _anchorColumn = ClampColumn(column);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cmd+click a header: commit the current rectangle and add a whole row/column (#28).</summary>
    public void AddRange(int top, int left, int bottom, int right)
    {
        _committed.Add(Range);
        _anchorRow = ClampRow(top);
        _anchorColumn = ClampColumn(left);
        ActiveRow = ClampRow(bottom);
        ActiveColumn = ClampColumn(right);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shift+click a row header: extend a full-width band from the anchored row to this one, so
    /// clicking row 2's header then Shift+clicking row 5's selects rows 2–5 entirely (#3).
    /// </summary>
    public void ExtendRowBand(int row)
    {
        _anchorColumn = 0;
        ActiveColumn = ClampColumn(SheetDimensions.MaxColumns - 1);
        ActiveRow = ClampRow(row);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shift+click a column header: extend a full-height band from the anchored column (#3).</summary>
    public void ExtendColumnBand(int column)
    {
        _anchorRow = 0;
        ActiveRow = ClampRow(SheetDimensions.MaxRows - 1);
        ActiveColumn = ClampColumn(column);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Extend the selection to a new active cell, keeping the anchor.</summary>
    public void ExtendTo(int row, int column)
    {
        ActiveRow = ClampRow(row);
        ActiveColumn = ClampColumn(column);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Select an explicit rectangle, anchored at its top-left and leaving the active cell at its
    /// <b>bottom-right</b> — as though the rectangle had just been dragged out from its first corner.
    /// <para>
    /// This comment used to say "top-left", which is not what the code below does and never was. Where the
    /// active cell has to be the <em>first</em> corner instead — a reference someone typed into the Name Box,
    /// say, where B2:C4 means "take me to B2" — use <see cref="Restore"/> and name the corner.
    /// </para>
    /// </summary>
    public void SetRange(int top, int left, int bottom, int right)
    {
        _committed.Clear();
        _anchorRow = ClampRow(top);
        _anchorColumn = ClampColumn(left);
        ActiveRow = ClampRow(bottom);
        ActiveColumn = ClampColumn(right);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Select a single rectangle with its active cell at the named corner, anchoring at the opposite one.
    /// Clears any disjoint ranges. Undo restores a remembered selection through this (#33), a merge puts the
    /// active cell on the merge's own origin, and the Name Box puts it on the corner the reference names.
    /// </summary>
    public void Restore(int top, int left, int bottom, int right, int activeRow, int activeColumn)
    {
        _committed.Clear();
        ActiveRow = ClampRow(activeRow);
        ActiveColumn = ClampColumn(activeColumn);
        _anchorRow = ClampRow(activeRow == top ? bottom : top);
        _anchorColumn = ClampColumn(activeColumn == left ? right : left);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static int ClampRow(int row) => Math.Clamp(row, 0, SheetDimensions.MaxRows - 1);

    private static int ClampColumn(int column) => Math.Clamp(column, 0, SheetDimensions.MaxColumns - 1);
}
