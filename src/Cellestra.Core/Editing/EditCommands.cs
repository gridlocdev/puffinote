using System.Globalization;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;

namespace Cellestra.Core.Editing;

/// <summary>Builders for the common edit commands. Each returns an undoable <see cref="IEditCommand"/>.</summary>
public static class EditCommands
{
    private static IReadOnlyList<(int, int)> Positions(CellRange range) => range.Positions().ToList();

    /// <summary>
    /// The cells an in-place edit must touch, clamped to the worksheet's used range. A whole-sheet
    /// or far-oversized selection (expanding ⌘A, PRD §10.1) would otherwise materialize billions of
    /// positions and hang/crash the app; intersecting with the used range bounds the work to the
    /// cells that actually exist. The mutation closures iterate the same clamped range.
    /// </summary>
    internal static IReadOnlyList<(int, int)> ClampedPositions(Worksheet sheet, CellRange range)
    {
        var clamped = ClampToUsed(sheet, range);
        return clamped is null ? Array.Empty<(int, int)>() : clamped.Value.Positions().ToList();
    }

    /// <summary>
    /// A small range is iterated verbatim (so styling still reaches empty cells the user picked);
    /// an oversized range — a whole-sheet or near-whole-sheet selection — is clamped to the used
    /// range so styling can't iterate billions of cells.
    /// </summary>
    private const int InPlaceCellCap = 1 << 20; // 1,048,576 — one full column's worth

    private static CellRange StyleRange(Worksheet sheet, CellRange range)
    {
        long cells = (long)range.RowCount * range.ColumnCount;
        if (cells <= InPlaceCellCap)
        {
            return range;
        }

        return ClampToUsed(sheet, range) ?? new CellRange(range.TopLeft);
    }

    private static IReadOnlyList<(int, int)> StylePositions(Worksheet sheet, CellRange range) =>
        StyleRange(sheet, range).Positions().ToList();

    /// <summary>Intersect a range with the worksheet's used range; null when nothing overlaps.</summary>
    internal static CellRange? ClampToUsed(Worksheet sheet, CellRange range)
    {
        if (sheet.GetUsedRange() is not { } used)
        {
            return null; // empty sheet: nothing to iterate
        }

        int top = Math.Max(range.Top, used.Top);
        int bottom = Math.Min(range.Bottom, used.Bottom);
        int left = Math.Max(range.Left, used.Left);
        int right = Math.Min(range.Right, used.Right);
        if (top > bottom || left > right)
        {
            return null;
        }

        return new CellRange(new CellAddress(left, top), new CellAddress(right, bottom));
    }

    /// <summary>Bundle several edits into one undoable step (see <see cref="CompositeEditCommand"/>).</summary>
    public static IEditCommand Composite(string label, IReadOnlyList<IEditCommand> commands) =>
        new CompositeEditCommand(label, commands);

    // ---- Cell content -----------------------------------------------------

    public static IEditCommand SetValue(string sheet, int row, int column, CellValue value) =>
        new CellSnapshotCommand("Set Value", sheet, new[] { (row, column) }, ws => ws.SetValue(row, column, value));

    public static IEditCommand SetFormula(string sheet, int row, int column, string formula) =>
        new CellSnapshotCommand("Set Formula", sheet, new[] { (row, column) },
            ws => ws.SetFormula(row, column, formula, CellValue.Empty));

    /// <summary>Set a cell from raw user input, choosing formula vs value (PRD §5).</summary>
    public static IEditCommand SetInput(string sheet, int row, int column, string text, CultureInfo culture) =>
        SetInput(null, sheet, row, column, text, culture);

    /// <summary>
    /// As <see cref="SetInput(string,int,int,string,CultureInfo)"/>, but with the workbook so a
    /// typed date can also stamp a date number-format onto a still-General cell (so it displays
    /// as a date, not a raw serial).
    /// </summary>
    public static IEditCommand SetInput(Workbook? workbook, string sheet, int row, int column, string text, CultureInfo culture)
    {
        var parsed = InputParser.Parse(text, culture);
        return new CellSnapshotCommand("Edit Cell", sheet, new[] { (row, column) }, ws =>
        {
            if (parsed.IsFormula)
            {
                ws.SetFormula(row, column, parsed.Formula!, CellValue.Empty);
                return;
            }

            ws.SetValue(row, column, parsed.Value);

            if (workbook is not null && parsed.SuggestedFormat is { } format)
            {
                var cell = ws.GetOrCreate(row, column);
                var style = workbook.Styles.Get(cell.StyleId);
                if (style.NumberFormat.IsGeneral)
                {
                    cell.StyleId = workbook.Styles.Intern(style.WithNumberFormat(format));
                }

                ws.PruneIfBlank(row, column, cell);
            }
        });
    }

    public static IEditCommand ClearContents(string sheet, CellRange range) =>
        new CellSnapshotCommand("Clear Contents", sheet, ws => ClampedPositions(ws, range), ws =>
        {
            foreach (var (row, column) in (ClampToUsed(ws, range) ?? range).Positions())
            {
                if (ws.Find(row, column) is not null)
                {
                    ws.SetValue(row, column, CellValue.Empty); // keeps style/hyperlink, drops value+formula
                }
            }
        });

    /// <summary>Reset every cell in the range to the default style, leaving its value/formula intact.</summary>
    public static IEditCommand ClearFormatting(Workbook workbook, string sheet, CellRange range) =>
        ApplyStyle(workbook, sheet, range, _ => CellStyle.Default, "Clear Formatting");

    /// <summary>Clear value, formula, style, and hyperlink — leaving the cells as if never touched.</summary>
    public static IEditCommand ClearAll(string sheet, CellRange range) =>
        new CellSnapshotCommand("Clear All", sheet, ws => ClampedPositions(ws, range), ws =>
        {
            foreach (var (row, column) in (ClampToUsed(ws, range) ?? range).Positions())
            {
                var cell = ws.Find(row, column);
                if (cell is null)
                {
                    continue;
                }

                cell.Formula = null;
                cell.Value = CellValue.Empty;
                cell.StyleId = StylePool.DefaultId;
                cell.Hyperlink = null;
                ws.PruneIfBlank(row, column, cell);
            }
        });

    /// <summary>Write a rectangular block of values starting at the top-left (paste/fill of literals).</summary>
    public static IEditCommand SetBlock(string sheet, int top, int left, CellValue[,] values, string label = "Paste")
    {
        int rows = values.GetLength(0);
        int columns = values.GetLength(1);
        var positions = new List<(int, int)>(rows * columns);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                positions.Add((top + r, left + c));
            }
        }

        return new CellSnapshotCommand(label, sheet, positions, ws =>
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    ws.SetValue(top + r, left + c, values[r, c]);
                }
            }
        });
    }

    // ---- Styling ----------------------------------------------------------

    /// <summary>Apply a style transform to every cell in a range (e.g. toggle bold).</summary>
    public static IEditCommand ApplyStyle(Workbook workbook, string sheet, CellRange range, Func<CellStyle, CellStyle> change, string label = "Format Cells") =>
        new CellSnapshotCommand(label, sheet, ws => StylePositions(ws, range), ws =>
        {
            foreach (var (row, column) in StyleRange(ws, range).Positions())
            {
                var cell = ws.GetOrCreate(row, column);
                cell.StyleId = workbook.Styles.Intern(change(workbook.Styles.Get(cell.StyleId)));
                ws.PruneIfBlank(row, column, cell);
            }
        });

    // ---- Layout -----------------------------------------------------------

    public static IEditCommand SetColumnWidth(string sheet, int column, double width)
    {
        double previous = 0;
        return new ActionCommand("Resize Column",
            wb =>
            {
                var col = Sheet(wb, sheet).GetOrCreateColumn(column);
                previous = col.Width;
                col.Width = width;
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).GetOrCreateColumn(column).Width = previous;
                return EditResult.None;
            });
    }

    public static IEditCommand SetRowHeight(string sheet, int row, double height)
    {
        double previous = 0;
        return new ActionCommand("Resize Row",
            wb =>
            {
                var model = Sheet(wb, sheet).GetOrCreateRow(row);
                previous = model.Height;
                model.Height = height;
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).GetOrCreateRow(row).Height = previous;
                return EditResult.None;
            });
    }

    public static IEditCommand SetColumnsHidden(string sheet, int firstColumn, int lastColumn, bool hidden)
    {
        bool[] previous = new bool[lastColumn - firstColumn + 1];
        return new ActionCommand(hidden ? "Hide Columns" : "Unhide Columns",
            wb =>
            {
                var ws = Sheet(wb, sheet);
                for (int c = firstColumn; c <= lastColumn; c++)
                {
                    var model = ws.GetOrCreateColumn(c);
                    previous[c - firstColumn] = model.Hidden;
                    model.Hidden = hidden;
                }

                return EditResult.None;
            },
            wb =>
            {
                var ws = Sheet(wb, sheet);
                for (int c = firstColumn; c <= lastColumn; c++)
                {
                    ws.GetOrCreateColumn(c).Hidden = previous[c - firstColumn];
                }

                return EditResult.None;
            });
    }

    public static IEditCommand SetRowsHidden(string sheet, int firstRow, int lastRow, bool hidden)
    {
        bool[] previous = new bool[lastRow - firstRow + 1];
        return new ActionCommand(hidden ? "Hide Rows" : "Unhide Rows",
            wb =>
            {
                var ws = Sheet(wb, sheet);
                for (int r = firstRow; r <= lastRow; r++)
                {
                    var model = ws.GetOrCreateRow(r);
                    previous[r - firstRow] = model.Hidden;
                    model.Hidden = hidden;
                }

                return EditResult.None;
            },
            wb =>
            {
                var ws = Sheet(wb, sheet);
                for (int r = firstRow; r <= lastRow; r++)
                {
                    ws.GetOrCreateRow(r).Hidden = previous[r - firstRow];
                }

                return EditResult.None;
            });
    }

    // ---- Merge ------------------------------------------------------------

    public static IEditCommand MergeCells(string sheet, CellRange range) =>
        new ActionCommand("Merge Cells",
            wb =>
            {
                Sheet(wb, sheet).AddMerge(new MergedRegion(range));
                return EditResult.None;
            },
            wb =>
            {
                Sheet(wb, sheet).RemoveMerge(range.Top, range.Left);
                return EditResult.None;
            });

    public static IEditCommand UnmergeCells(string sheet, int row, int column)
    {
        MergedRegion? removed = null;
        return new ActionCommand("Unmerge Cells",
            wb =>
            {
                var ws = Sheet(wb, sheet);
                removed = ws.FindMerge(row, column);
                if (removed is not null)
                {
                    ws.RemoveMerge(row, column);
                }

                return EditResult.None;
            },
            wb =>
            {
                if (removed is not null)
                {
                    Sheet(wb, sheet).AddMerge(removed);
                }

                return EditResult.None;
            });
    }

    private static Worksheet Sheet(Workbook workbook, string name) =>
        workbook.FindSheet(name) ?? throw new InvalidOperationException($"Sheet '{name}' not found.");
}
