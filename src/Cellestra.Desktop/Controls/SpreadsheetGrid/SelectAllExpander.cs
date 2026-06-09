using Cellestra.Core.Data;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Pure logic for expanding Select-All (⌘A) — PRD §10.1. The first press selects the contiguous
/// data region around the active cell; pressing again (when the selection already covers that
/// region) widens to the whole sheet. This matches Excel / Google Sheets / LibreOffice Calc.
/// Kept side-effect free so it can be unit-tested without driving the grid or the command.
/// </summary>
public static class SelectAllExpander
{
    /// <summary>The whole-sheet rectangle (A1 to the last cell).</summary>
    public static CellRange WholeSheet { get; } = new(
        new CellAddress(0, 0),
        new CellAddress(SheetDimensions.MaxColumns - 1, SheetDimensions.MaxRows - 1));

    /// <summary>
    /// Given the current selection and the active cell, return the next Select-All rectangle:
    /// the data region first, then the whole sheet once the selection already matches that region.
    /// </summary>
    public static CellRange Next(Worksheet sheet, CellRange current, int activeRow, int activeColumn)
    {
        var region = DataRegion.CurrentRegion(sheet, activeRow, activeColumn);

        // Already covering the data region (or already whole-sheet) → escalate to the whole sheet.
        return Covers(current, region) ? WholeSheet : region;
    }

    private static bool Covers(CellRange a, CellRange b) =>
        a.Top == b.Top && a.Left == b.Left && a.Bottom == b.Bottom && a.Right == b.Right;
}
