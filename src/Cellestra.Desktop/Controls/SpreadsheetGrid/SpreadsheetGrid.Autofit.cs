using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Cellestra.Core;
using Cellestra.Core.Editing;
using Cellestra.Core.Styling;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Double-clicking a column header's right border autofits the column to its content (#23). The
/// width is measured over the column's populated cells (an empty column falls back to the default
/// width) and applied through the undoable <see cref="EditCommands.SetColumnWidth"/>.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private const double BorderGrab = 4;

    private bool TryHitColumnBorder(Point point, out int column)
    {
        column = -1;
        if (point.Y >= HeaderHeight)
        {
            return false; // only the column-header strip
        }

        foreach (var (index, x, w) in _visibleColumns)
        {
            if (Math.Abs(point.X - (x + w)) <= BorderGrab)
            {
                column = index;
                return true;
            }
        }

        return false;
    }

    private void AutofitColumn(int column)
    {
        var sheet = Sheet;
        if (_session is null || sheet is null)
        {
            return;
        }

        double widest = MeasureWidestInColumn(sheet, column);
        double target = widest > 0 ? Math.Clamp(widest + 10, 24, 600) : Defaults.ColumnWidth;
        _session.Execute(EditCommands.SetColumnWidth(sheet.Name, column, target));
        Redraw();
    }

    /// <summary>
    /// Widen each column in the range to fit its content if it currently clips — never shrink it.
    /// Used after a one-tap number format (e.g. a date) so the result shows rather than reading #####.
    /// </summary>
    internal void GrowColumnsToFit(int left, int right)
    {
        var sheet = Sheet;
        if (_session is null || sheet is null)
        {
            return;
        }

        bool changed = false;
        for (int column = left; column <= right; column++)
        {
            double widest = MeasureWidestInColumn(sheet, column);
            if (widest <= 0)
            {
                continue;
            }

            double target = Math.Clamp(widest + 10, 24, 600);
            if (target > sheet.GetColumnWidth(column) + 0.5)
            {
                _session.Execute(EditCommands.SetColumnWidth(sheet.Name, column, target));
                changed = true;
            }
        }

        if (changed)
        {
            Redraw();
        }
    }

    /// <summary>The widest formatted cell content in a column, measured in model space (no zoom). 0 if empty.</summary>
    private double MeasureWidestInColumn(Cellestra.Core.Model.Worksheet sheet, int column)
    {
        double widest = 0;
        foreach (var (_, col, cell) in sheet.EnumerateCells())
        {
            if (col != column || cell.Value.IsEmpty)
            {
                continue;
            }

            var style = _session!.Workbook.Styles.Get(cell.StyleId);
            string text = NumberFormatter.Format(cell.Value, style.NumberFormat, Culture);
            if (text.Length == 0)
            {
                continue;
            }

            var typeface = new Typeface(
                style.Font.Family,
                style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
                style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);

            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, style.Font.SizePoints * 96.0 / 72.0, Brushes.Black);
            widest = Math.Max(widest, formatted.Width);
        }

        return widest;
    }
}
