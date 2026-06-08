using System;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// A transient, non-committing style overlay used for live previews from the command bar's drawers:
/// while the user hovers a font size or colour swatch, the affected cells render with the would-be
/// style, but nothing touches the model or the undo stack (PRD §8.5). Clearing it restores the
/// committed look. The render path resolves styles through <see cref="ResolveStyle"/>.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private (CellRange Range, Func<CellStyle, CellStyle> Transform)? _stylePreview;

    /// <summary>Preview <paramref name="transform"/> over <paramref name="range"/> until cleared. Purely visual.</summary>
    internal void SetStylePreview(CellRange range, Func<CellStyle, CellStyle> transform)
    {
        _stylePreview = (range, transform);
        Redraw();
    }

    /// <summary>Drop any active preview and repaint the committed styles.</summary>
    internal void ClearStylePreview()
    {
        if (_stylePreview is not null)
        {
            _stylePreview = null;
            Redraw();
        }
    }

    /// <summary>The committed style of a cell, with the live preview applied on top when it covers the cell.</summary>
    private CellStyle ResolveStyle(Cell cell, int row, int column)
    {
        var style = _session!.Workbook.Styles.Get(cell.StyleId);
        return _stylePreview is { } preview && preview.Range.Contains(row, column)
            ? preview.Transform(style)
            : style;
    }
}
