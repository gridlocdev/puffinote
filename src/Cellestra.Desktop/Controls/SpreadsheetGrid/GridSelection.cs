using System;
using System.Collections.Generic;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The current cell selection: an active cell plus a rectangular range (anchored when the user
/// extends with Shift), optionally with additional disjoint ranges added by Cmd+click (#27/#28).
/// The active cell lives in the most recent range. Pure UI state — never undoable (PRD §8.5).
/// </summary>
public sealed class GridSelection
{
    private int _activeRow;
    private int _activeColumn;
    private int _anchorRow;
    private int _anchorColumn;

    // Ranges committed by earlier Cmd+clicks; the live anchor/active rectangle is appended to these.
    private readonly List<CellRange> _committed = new();

    public int ActiveRow => _activeRow;

    public int ActiveColumn => _activeColumn;

    public int Top => Math.Min(_anchorRow, _activeRow);

    public int Bottom => Math.Max(_anchorRow, _activeRow);

    public int Left => Math.Min(_anchorColumn, _activeColumn);

    public int Right => Math.Max(_anchorColumn, _activeColumn);

    public bool IsSingleCell => Top == Bottom && Left == Right;

    public event EventHandler? Changed;

    public CellAddress ActiveAddress => new(_activeColumn, _activeRow);

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
        _activeRow = _anchorRow = ClampRow(row);
        _activeColumn = _anchorColumn = ClampColumn(column);
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
        _activeRow = _anchorRow = ClampRow(row);
        _activeColumn = _anchorColumn = ClampColumn(column);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cmd+click a header: commit the current rectangle and add a whole row/column (#28).</summary>
    public void AddRange(int top, int left, int bottom, int right)
    {
        _committed.Add(Range);
        _anchorRow = ClampRow(top);
        _anchorColumn = ClampColumn(left);
        _activeRow = ClampRow(bottom);
        _activeColumn = ClampColumn(right);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shift+click a row header: extend a full-width band from the anchored row to this one, so
    /// clicking row 2's header then Shift+clicking row 5's selects rows 2–5 entirely (#3).
    /// </summary>
    public void ExtendRowBand(int row)
    {
        _anchorColumn = 0;
        _activeColumn = ClampColumn(SheetDimensions.MaxColumns - 1);
        _activeRow = ClampRow(row);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shift+click a column header: extend a full-height band from the anchored column (#3).</summary>
    public void ExtendColumnBand(int column)
    {
        _anchorRow = 0;
        _activeRow = ClampRow(SheetDimensions.MaxRows - 1);
        _activeColumn = ClampColumn(column);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Extend the selection to a new active cell, keeping the anchor.</summary>
    public void ExtendTo(int row, int column)
    {
        _activeRow = ClampRow(row);
        _activeColumn = ClampColumn(column);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Select an explicit rectangle, leaving the active cell at its top-left.</summary>
    public void SetRange(int top, int left, int bottom, int right)
    {
        _committed.Clear();
        _anchorRow = ClampRow(top);
        _anchorColumn = ClampColumn(left);
        _activeRow = ClampRow(bottom);
        _activeColumn = ClampColumn(right);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Restore a single rectangle with its active cell at the given corner (used by undo, #33).
    /// Clears any disjoint ranges.
    /// </summary>
    public void Restore(int top, int left, int bottom, int right, int activeRow, int activeColumn)
    {
        _committed.Clear();
        _activeRow = ClampRow(activeRow);
        _activeColumn = ClampColumn(activeColumn);
        _anchorRow = ClampRow(activeRow == top ? bottom : top);
        _anchorColumn = ClampColumn(activeColumn == left ? right : left);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static int ClampRow(int row) => Math.Clamp(row, 0, SheetDimensions.MaxRows - 1);

    private static int ClampColumn(int column) => Math.Clamp(column, 0, SheetDimensions.MaxColumns - 1);
}
