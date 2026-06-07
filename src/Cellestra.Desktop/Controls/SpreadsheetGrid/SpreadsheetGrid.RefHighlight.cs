using System.Collections.Generic;
using Avalonia.Media;
using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Live reference highlighting (#18): while editing a formula, the cells and ranges it references
/// are outlined on the grid, each in a distinct colour cycled from a small palette. (Colouring the
/// matching tokens inside the editor text would need a rich-text editor, which the plain TextBox
/// isn't — left for later.)
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private static readonly Color[] ReferenceColors =
    {
        Color.FromRgb(0x1A, 0x73, 0xE8), // blue
        Color.FromRgb(0x1E, 0x88, 0x4E), // green
        Color.FromRgb(0xE8, 0x71, 0x0A), // orange
        Color.FromRgb(0x8E, 0x24, 0xAA), // purple
        Color.FromRgb(0x00, 0x83, 0x9B), // teal
        Color.FromRgb(0xC6, 0x28, 0x28), // red
    };

    private void DrawReferenceHighlights(DrawingContext context)
    {
        if (!_editing || Sheet is null)
        {
            return;
        }

        string text = _editor.Text ?? string.Empty;
        if (text.Length < 2 || text[0] != '=' || !Parser.TryParse(text[1..], out var expr, out _) || expr is null)
        {
            return;
        }

        var ranges = new List<CellRange>();
        CollectRanges(expr, ranges);

        for (int i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            if (range.Sheet is not null && !string.Equals(range.Sheet, Sheet.Name, System.StringComparison.OrdinalIgnoreCase))
            {
                continue; // a reference to another sheet can't be shown here
            }

            if (TryGetRegionRect(range.Top, range.Left, range.Bottom, range.Right, out var rect))
            {
                var pen = new Pen(new SolidColorBrush(ReferenceColors[i % ReferenceColors.Length]), 1.5);
                context.DrawRectangle(null, pen, rect.Deflate(0.5));
            }
        }
    }

    private static void CollectRanges(Expr expr, List<CellRange> into)
    {
        switch (expr)
        {
            case ReferenceExpr reference:
                into.Add(new CellRange(reference.Address));
                break;
            case RangeExpr range:
                into.Add(range.Range);
                break;
            case UnaryExpr unary:
                CollectRanges(unary.Operand, into);
                break;
            case BinaryExpr binary:
                CollectRanges(binary.Left, into);
                CollectRanges(binary.Right, into);
                break;
            case FunctionCallExpr call:
                foreach (var argument in call.Arguments)
                {
                    CollectRanges(argument, into);
                }

                break;
        }
    }
}
