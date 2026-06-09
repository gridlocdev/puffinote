using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Surfaces an error cell's plain-English <see cref="Cellestra.Core.Values.CellError.Reason"/>
/// (PRD §7 heuristic #9) when the pointer hovers it — the grid otherwise draws only the terse token
/// (e.g. <c>#REF!</c>). Implemented as a standard Avalonia <see cref="ToolTip"/> attached to the
/// grid; the tip text is swapped as the pointer moves between cells, and cleared off an error cell.
/// Like the signature hint, the tip is non-interactive and never traps hit-testing.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    // The cell whose reason the tip currently shows, so we only touch the ToolTip when it changes.
    private (int Row, int Column)? _errorTipCell;

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

    /// <summary>Update the hover tooltip to the error reason under the pointer (or hide it).</summary>
    private void UpdateErrorTip(Point point)
    {
        if (!HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader) || rowHeader || columnHeader)
        {
            ClearErrorTip();
            return;
        }

        string? reason = ErrorReasonAt(row, column);
        if (reason is null)
        {
            ClearErrorTip();
            return;
        }

        if (_errorTipCell == (row, column))
        {
            return; // same error cell — already showing its reason
        }

        _errorTipCell = (row, column);
        ToolTip.SetTip(this, reason);
        ToolTip.SetIsOpen(this, true);
    }

    private void ClearErrorTip()
    {
        if (_errorTipCell is null)
        {
            return;
        }

        _errorTipCell = null;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }
}
