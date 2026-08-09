using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Editing;

/// <summary>One captured cell: its value, formula source (null for a literal), and style index.</summary>
public sealed record ClipboardCell(CellValue Value, string? Formula, int StyleId);

/// <summary>
/// A rectangular snapshot of cells for copy/cut — carrying values, formulas, and styles (not just
/// the displayed text), so an in-app paste reproduces the source faithfully (PRD §3 "feel").
/// Records where it came from and whether it was a cut, so paste can adjust references (copy) or
/// move them unchanged (cut) and consume a cut once.
/// </summary>
public sealed class ClipboardBlock
{
    public ClipboardBlock(string sourceSheet, CellRange sourceRange, bool isCut, ClipboardCell?[,] cells)
    {
        SourceSheet = sourceSheet;
        SourceRange = sourceRange;
        IsCut = isCut;
        Cells = cells;
    }

    public string SourceSheet { get; }

    public CellRange SourceRange { get; }

    public bool IsCut { get; }

    /// <summary>The captured cells, <c>[row, column]</c>; a null entry was blank.</summary>
    public ClipboardCell?[,] Cells { get; }

    public int Rows => Cells.GetLength(0);

    public int Columns => Cells.GetLength(1);

    /// <summary>
    /// The rectangle a copy of <paramref name="range"/> actually captures. An expanding ⌘A ends at the whole
    /// sheet — 16384 × 1048576 cells — and a rectangle that size is not a big copy, it is 17 billion array
    /// slots and a hang. Excel and Calc do not materialise it either: both hold a selection as a rectangle
    /// (Calc's <c>ScMarkData::aMarkRange</c>) and a copy takes the sheet's used cells, which is what this
    /// clamps to. Anything at or under the cap is taken verbatim, so a deliberately-selected block of blank
    /// cells still copies as blanks.
    /// </summary>
    public static CellRange CopyRange(Worksheet sheet, CellRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        long cells = (long)range.RowCount * range.ColumnCount;
        return cells <= EditCommands.OversizedCellCap
            ? range
            : EditCommands.ClampToUsed(sheet, range) ?? new CellRange(range.TopLeft);
    }

    public static ClipboardBlock Capture(Worksheet sheet, CellRange range, bool isCut)
    {
        range = CopyRange(sheet, range);
        var cells = new ClipboardCell?[range.RowCount, range.ColumnCount];
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColumnCount; c++)
            {
                var cell = sheet.Find(range.Top + r, range.Left + c);
                cells[r, c] = cell is null
                    ? null
                    : new ClipboardCell(cell.Value, cell.Formula, cell.StyleId);
            }
        }

        return new ClipboardBlock(sheet.Name, range, isCut, cells);
    }
}
