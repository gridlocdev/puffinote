using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The grid's single hover tooltip. It surfaces an error cell's plain-English
/// <see cref="Cellestra.Core.Values.CellError.Reason"/> (PRD §7 heuristic #9) — the grid otherwise
/// draws only the terse token (e.g. <c>#REF!</c>) — and, for a hyperlinked cell, a discoverability
/// hint ("⌘-click to open …", PRD §7 heuristic #6). Implemented as a standard Avalonia
/// <see cref="ToolTip"/> attached to the grid; the tip text is swapped as the pointer moves between
/// cells, and cleared off a cell with neither. Like the signature hint, the tip is non-interactive
/// and never traps hit-testing.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    // The cell whose tip is currently shown, so we only touch the ToolTip when it changes.
    private (int Row, int Column)? _tipCell;

    /// <summary>Test seam: the error reason the grid would show for a hover at this cell, or null.</summary>
    internal string? ErrorReasonAt(int row, int column)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return null;
        }

        // An error inside a merge is reported by its anchor cell.
        if (sheet.FindMerge(row, column) is { } merge)
        {
            row = merge.Range.Top;
            column = merge.Range.Left;
        }

        var cell = sheet.Find(row, column);
        return cell is not null && cell.Value.IsError ? cell.Value.AsError.Reason : null;
    }

    /// <summary>
    /// The hover tip for a cell: an error reason takes priority; otherwise a hyperlinked cell shows
    /// its "⌘-click to open …" hint. Null when the cell has neither.
    /// </summary>
    internal string? HoverTipAt(int row, int column) => ErrorReasonAt(row, column) ?? HyperlinkTipAt(row, column);

    /// <summary>Update the hover tooltip to the text under the pointer (or hide it).</summary>
    private void UpdateHoverTip(Point point)
    {
        if (!HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader) || rowHeader || columnHeader)
        {
            ClearHoverTip();
            return;
        }

        string? tip = HoverTipAt(row, column);
        if (tip is null)
        {
            ClearHoverTip();
            return;
        }

        if (_tipCell == (row, column))
        {
            return; // same cell — already showing its tip
        }

        _tipCell = (row, column);
        ToolTip.SetTip(this, tip);
        ToolTip.SetIsOpen(this, true);
    }

    private void ClearHoverTip()
    {
        if (_tipCell is null)
        {
            return;
        }

        _tipCell = null;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }
}
