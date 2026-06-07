using System;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The current cell selection: an active cell plus a rectangular range (anchored when the
/// user extends with Shift). Pure UI state — never undoable (PRD §8.5).
/// </summary>
public sealed class GridSelection
{
    private int _activeRow;
    private int _activeColumn;
    private int _anchorRow;
    private int _anchorColumn;

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

    /// <summary>Move the active cell and collapse the selection to it.</summary>
    public void MoveTo(int row, int column)
    {
        _activeRow = _anchorRow = ClampRow(row);
        _activeColumn = _anchorColumn = ClampColumn(column);
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
        _anchorRow = ClampRow(top);
        _anchorColumn = ClampColumn(left);
        _activeRow = ClampRow(bottom);
        _activeColumn = ClampColumn(right);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static int ClampRow(int row) => Math.Clamp(row, 0, SheetDimensions.MaxRows - 1);

    private static int ClampColumn(int column) => Math.Clamp(column, 0, SheetDimensions.MaxColumns - 1);
}
