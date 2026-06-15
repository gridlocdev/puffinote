using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using SAlign = Cellestra.Core.Styling.HorizontalAlignment;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Overlay scroll bars (task 3). The grid scrolls by whole row/column index, so each bar maps its
/// value 1:1 onto <see cref="_scrollRow"/>/<see cref="_scrollColumn"/>. A bar appears only when the
/// content stretches past the viewport in that direction — where "content" is the used range plus any
/// text that spills rightward past its cell into empty neighbours (so a long label still pulls the
/// horizontal bar out). Frozen rows/columns stay pinned and sit outside the scrollable span.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private const double ScrollThickness = 12;
    private const int OverflowScanCellCap = 5000; // skip the per-cell spill measure on very large sheets

    private readonly ScrollBar _vScroll = new()
    {
        Orientation = Orientation.Vertical,
        Width = ScrollThickness,
        IsVisible = false,
        AllowAutoHide = false,
    };

    private readonly ScrollBar _hScroll = new()
    {
        Orientation = Orientation.Horizontal,
        Height = ScrollThickness,
        IsVisible = false,
        AllowAutoHide = false,
    };

    // Content extent in row/column counts, cached against a cheap signature so the per-cell overflow
    // scan only re-runs when the sheet's content actually changes (not on every scroll/redraw).
    private int _extentRows;
    private int _extentColumns;
    private (int Bottom, int Right, int Count, string? Sheet) _extentSignature = (-2, -2, -2, null);

    // ---- Test seams (headless UI tests) ----
    internal bool VerticalScrollBarVisibleForTest => _vScroll.IsVisible;

    internal bool HorizontalScrollBarVisibleForTest => _hScroll.IsVisible;

    internal double VerticalScrollMaxForTest => _vScroll.Maximum;

    internal double HorizontalScrollMaxForTest => _hScroll.Maximum;

    internal int ScrollRowForTest => _scrollRow;

    internal int ScrollColumnForTest => _scrollColumn;

    /// <summary>Drive the vertical bar as a user drag would (value is a scrollable-row offset).</summary>
    internal void DragVerticalScrollForTest(double value) =>
        ScrollToRow((int)Math.Round(value) + (Sheet?.FrozenRows ?? 0));

    private void InitScrollBars()
    {
        // Scroll fires only on user interaction (drag/track/buttons), never when we set Value
        // ourselves — so there's no echo to guard against.
        _vScroll.Scroll += (_, e) => ScrollToRow((int)Math.Round(e.NewValue) + (Sheet?.FrozenRows ?? 0));
        _hScroll.Scroll += (_, e) => ScrollToColumn((int)Math.Round(e.NewValue) + (Sheet?.FrozenColumns ?? 0));
        Children.Add(_vScroll);
        Children.Add(_hScroll);
    }

    /// <summary>Whether a point falls on a visible overlay scroll bar (its strip along an edge).</summary>
    private bool IsOverScrollBar(Point point) =>
        (_vScroll.IsVisible && point.X >= Bounds.Width - ScrollThickness) ||
        (_hScroll.IsVisible && point.Y >= Bounds.Height - ScrollThickness);

    private void ScrollToRow(int row)
    {
        int frozen = Sheet?.FrozenRows ?? 0;
        int clamped = Math.Clamp(row, frozen, SheetDimensions.MaxRows - 1);
        if (clamped != _scrollRow)
        {
            _scrollRow = clamped;
            Redraw();
            InvalidateArrange();
        }
    }

    private void ScrollToColumn(int column)
    {
        int frozen = Sheet?.FrozenColumns ?? 0;
        int clamped = Math.Clamp(column, frozen, SheetDimensions.MaxColumns - 1);
        if (clamped != _scrollColumn)
        {
            _scrollColumn = clamped;
            Redraw();
            InvalidateArrange();
        }
    }

    /// <summary>Position the bars along the right/bottom edges and refresh their range from the layout.</summary>
    private void LayoutScrollBars(Size bounds)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            _vScroll.IsVisible = _hScroll.IsVisible = false;
            return;
        }

        UpdateExtent(sheet);

        // Viewport span = visible scrollable (non-frozen) rows/columns from the last built layout.
        int frozenRows = sheet.FrozenRows, frozenColumns = sheet.FrozenColumns;
        int visibleRows = CountScrollable(_visibleRows, frozenRows);
        int visibleColumns = CountScrollable(_visibleColumns, frozenColumns);

        // Max scroll index = the first row/column that, placed at the top of the scrollable area,
        // brings the last content line to the bottom edge of the viewport.
        int maxRow = Math.Max(0, _extentRows - frozenRows - visibleRows);
        int maxColumn = Math.Max(0, _extentColumns - frozenColumns - visibleColumns);

        bool showV = maxRow > 0;
        bool showH = maxColumn > 0;

        _vScroll.IsVisible = showV;
        _hScroll.IsVisible = showH;

        // Leave the corner clear when both bars show, so neither thumb runs under the other.
        double vBottom = bounds.Height - (showH ? ScrollThickness : 0);
        double hRight = bounds.Width - (showV ? ScrollThickness : 0);

        if (showV)
        {
            _vScroll.Minimum = 0;
            _vScroll.Maximum = maxRow;
            _vScroll.ViewportSize = Math.Max(1, visibleRows);
            _vScroll.LargeChange = Math.Max(1, visibleRows);
            _vScroll.SmallChange = 1;
            _vScroll.Value = Math.Clamp(_scrollRow - frozenRows, 0, maxRow);
            var rect = new Rect(bounds.Width - ScrollThickness, HeaderHeight, ScrollThickness, Math.Max(0, vBottom - HeaderHeight));
            _vScroll.Measure(rect.Size);
            _vScroll.Arrange(rect);
        }

        if (showH)
        {
            _hScroll.Minimum = 0;
            _hScroll.Maximum = maxColumn;
            _hScroll.ViewportSize = Math.Max(1, visibleColumns);
            _hScroll.LargeChange = Math.Max(1, visibleColumns);
            _hScroll.SmallChange = 1;
            _hScroll.Value = Math.Clamp(_scrollColumn - frozenColumns, 0, maxColumn);
            var rect = new Rect(HeaderWidth, bounds.Height - ScrollThickness, Math.Max(0, hRight - HeaderWidth), ScrollThickness);
            _hScroll.Measure(rect.Size);
            _hScroll.Arrange(rect);
        }
    }

    private static int CountScrollable(System.Collections.Generic.List<(int Index, double Offset, double Size)> visible, int frozen)
    {
        int count = 0;
        foreach (var (index, _, _) in visible)
        {
            if (index >= frozen)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Recompute the content extent (rows/columns) when the sheet's content signature changes.</summary>
    private void UpdateExtent(Worksheet sheet)
    {
        var used = sheet.GetUsedRange();
        var signature = (used?.Bottom ?? -1, used?.Right ?? -1, sheet.CellCount, sheet.Name);
        if (signature == _extentSignature)
        {
            return;
        }

        _extentSignature = signature;

        if (used is null)
        {
            _extentRows = _extentColumns = 0;
            return;
        }

        _extentRows = used.Value.Bottom + 1;
        int columns = used.Value.Right + 1;

        // Pull the horizontal extent out to wherever long left-aligned text spills (task 3). Skipped
        // on very large sheets to keep the scan cheap; the used range still drives the bar there.
        if (sheet.CellCount <= OverflowScanCellCap)
        {
            foreach (var (row, column, cell) in sheet.EnumerateCells())
            {
                if (cell.Value.IsEmpty)
                {
                    continue;
                }

                int end = SpillEndColumn(sheet, row, column, cell);
                if (end + 1 > columns)
                {
                    columns = end + 1;
                }
            }
        }

        _extentColumns = columns;
    }

    /// <summary>
    /// The last column a cell's text reaches when it spills rightward across empty neighbours, or the
    /// cell's own column when it fits (mirrors the draw-time spill rule, in model space).
    /// </summary>
    private int SpillEndColumn(Worksheet sheet, int row, int column, Cell cell)
    {
        if (_session is null)
        {
            return column;
        }

        bool numeric = cell.Value.IsNumber || cell.Value.IsBoolean || cell.Value.IsError;
        var style = _session.Workbook.Styles.Get(cell.StyleId);
        if (numeric || style.Alignment.WrapText)
        {
            return column; // numbers show #####, wrapped text grows the row — neither spills
        }

        var horizontal = style.Alignment.Horizontal;
        if (horizontal is not (SAlign.Left or SAlign.General))
        {
            return column; // only left/general text spills rightward
        }

        double textWidth = MeasureCellWidth(sheet, cell);
        double available = EffectiveColumnWidth(sheet, column) - 2 * CellPadX;
        if (textWidth <= available)
        {
            return column;
        }

        int end = column;
        for (int c = column + 1; c < SheetDimensions.MaxColumns && textWidth > available; c++)
        {
            var neighbour = sheet.Find(row, c);
            if ((neighbour is not null && !neighbour.Value.IsEmpty) || sheet.FindMerge(row, c) is not null)
            {
                break; // an occupied/merged neighbour clips the spill
            }

            available += EffectiveColumnWidth(sheet, c);
            end = c;
        }

        return end;
    }
}
