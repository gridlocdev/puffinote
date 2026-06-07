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

        double widest = 0;
        foreach (var (_, col, cell) in sheet.EnumerateCells())
        {
            if (col != column || cell.Value.IsEmpty)
            {
                continue;
            }

            var style = _session.Workbook.Styles.Get(cell.StyleId);
            string text = NumberFormatter.Format(cell.Value, style.NumberFormat, Culture);
            if (text.Length == 0)
            {
                continue;
            }

            var typeface = new Typeface(
                style.Font.Family,
                style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
                style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);

            // Measure in model space (no zoom) since the stored width is unzoomed.
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, style.Font.SizePoints * 96.0 / 72.0, Brushes.Black);
            widest = Math.Max(widest, formatted.Width);
        }

        double target = widest > 0 ? Math.Clamp(widest + 10, 24, 600) : Defaults.ColumnWidth;
        _session.Execute(EditCommands.SetColumnWidth(sheet.Name, column, target));
        Redraw();
    }
}
