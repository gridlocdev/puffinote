using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;

namespace Cellestra.Core.Editing;

/// <summary>
/// A captured block of formatting for the Format Painter: the <see cref="CellStyle"/> of each cell
/// in a source range plus that range's merge layout. Applying it stamps ONLY the formatting (font,
/// fill, alignment, number format, borders) — never values or formulas — onto a target range,
/// tiling the source pattern like copy/paste and replicating merge-cells behavior. Mirrors how
/// <see cref="ClipboardBlock"/> captures cells, but for style instead of content.
/// </summary>
public sealed class FormatBlock
{
    private FormatBlock(int rows, int columns, CellStyle[,] styles, IReadOnlyList<(int Row, int Column)> mergeAnchors, IReadOnlyList<(int Rows, int Columns)> mergeSpans)
    {
        Rows = rows;
        Columns = columns;
        Styles = styles;
        MergeAnchors = mergeAnchors;
        MergeSpans = mergeSpans;
    }

    public int Rows { get; }

    public int Columns { get; }

    /// <summary>The captured style for each cell, <c>[row, column]</c>, relative to the source top-left.</summary>
    public CellStyle[,] Styles { get; }

    /// <summary>Source-relative top-left of each merge that lay within the captured range.</summary>
    public IReadOnlyList<(int Row, int Column)> MergeAnchors { get; }

    /// <summary>The (rows, columns) span of each captured merge, paired by index with <see cref="MergeAnchors"/>.</summary>
    public IReadOnlyList<(int Rows, int Columns)> MergeSpans { get; }

    /// <summary>Capture the styles and merge layout of <paramref name="range"/> on <paramref name="sheet"/>.</summary>
    public static FormatBlock Capture(Workbook workbook, Worksheet sheet, CellRange range)
    {
        var styles = new CellStyle[range.RowCount, range.ColumnCount];
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColumnCount; c++)
            {
                var cell = sheet.Find(range.Top + r, range.Left + c);
                styles[r, c] = workbook.Styles.Get(cell?.StyleId ?? StylePool.DefaultId);
            }
        }

        var anchors = new List<(int, int)>();
        var spans = new List<(int, int)>();
        foreach (var region in sheet.MergedRegions)
        {
            // Keep merges whose anchor sits inside the captured range; they reproduce relative to it.
            if (range.Contains(region.Range.Top, region.Range.Left))
            {
                anchors.Add((region.Range.Top - range.Top, region.Range.Left - range.Left));
                spans.Add((region.Range.RowCount, region.Range.ColumnCount));
            }
        }

        return new FormatBlock(range.RowCount, range.ColumnCount, styles, anchors, spans);
    }

    /// <summary>
    /// An undoable command that paints this block's formatting over <paramref name="target"/>,
    /// tiling the source pattern. Existing target merges are cleared and the source's merges are
    /// recreated at the matching offsets (clamped to the target).
    /// </summary>
    public IEditCommand ApplyTo(string sheet, CellRange target) =>
        new PaintCommand(this, sheet, target);

    /// <summary>
    /// The rectangle a drag-to-paint covers: the bounding box of the press cell
    /// (<paramref name="anchorRow"/>, <paramref name="anchorColumn"/>) and the cell the pointer is
    /// currently over (<paramref name="row"/>, <paramref name="column"/>). A click without a drag
    /// (both cells the same) yields that single cell. Pure so the grid's pointer code stays a thin
    /// call over a unit-tested helper.
    /// </summary>
    public static CellRange PaintTarget(int anchorRow, int anchorColumn, int row, int column) =>
        new(
            new CellAddress(Math.Min(anchorColumn, column), Math.Min(anchorRow, row)),
            new CellAddress(Math.Max(anchorColumn, column), Math.Max(anchorRow, row)));

    private sealed class PaintCommand : IEditCommand
    {
        private readonly FormatBlock _block;
        private readonly string _sheetName;
        private readonly CellRange _target;
        private List<(int Row, int Column, Cell? Before)>? _cellSnapshot;
        private List<MergedRegion>? _mergeSnapshot;

        public PaintCommand(FormatBlock block, string sheetName, CellRange target)
        {
            _block = block;
            _sheetName = sheetName;
            _target = target;
        }

        public string Label => "Format Painter";

        public EditResult Apply(Workbook workbook)
        {
            var sheet = workbook.FindSheet(_sheetName)
                ?? throw new InvalidOperationException($"Sheet '{_sheetName}' not found.");

            // Snapshot every target cell's full state and the sheet's merges so undo is exact.
            _cellSnapshot = new List<(int, int, Cell?)>(_target.RowCount * _target.ColumnCount);
            foreach (var (row, column) in _target.Positions())
            {
                _cellSnapshot.Add((row, column, sheet.Find(row, column)?.Clone()));
            }

            _mergeSnapshot = sheet.MergedRegions.ToList();

            // Stamp styles, tiling the source pattern over the target.
            var changed = new List<CellRef>(_target.RowCount * _target.ColumnCount);
            for (int r = 0; r < _target.RowCount; r++)
            {
                for (int c = 0; c < _target.ColumnCount; c++)
                {
                    int row = _target.Top + r;
                    int column = _target.Left + c;
                    var style = _block.Styles[r % _block.Rows, c % _block.Columns];
                    var cell = sheet.GetOrCreate(row, column);
                    cell.StyleId = workbook.Styles.Intern(style);
                    sheet.PruneIfBlank(row, column, cell);
                    changed.Add(new CellRef(_sheetName, row, column));
                }
            }

            ApplyMerges(sheet);
            return EditResult.Cells(changed);
        }

        public EditResult Undo(Workbook workbook)
        {
            var sheet = workbook.FindSheet(_sheetName)
                ?? throw new InvalidOperationException($"Sheet '{_sheetName}' not found.");

            if (_cellSnapshot is null || _mergeSnapshot is null)
            {
                return EditResult.None;
            }

            foreach (var (row, column, before) in _cellSnapshot)
            {
                sheet.PutCell(row, column, before?.Clone());
            }

            sheet.ReplaceMerges(_mergeSnapshot);

            var changed = _cellSnapshot.Select(s => new CellRef(_sheetName, s.Row, s.Column)).ToList();
            return EditResult.Cells(changed);
        }

        private void ApplyMerges(Worksheet sheet)
        {
            if (_block.MergeAnchors.Count == 0)
            {
                return;
            }

            // Clear any merge overlapping the target so the painted pattern wins (mirrors paste).
            foreach (var region in sheet.MergedRegions.ToList())
            {
                if (region.Range.Intersects(_target))
                {
                    sheet.RemoveMerge(region.Range.Top, region.Range.Left);
                }
            }

            // Recreate the source's merges, tiled to align with the stamped style pattern, clamped
            // to the target so a partial tile never spills a merge outside the painted area.
            var placed = new List<CellRange>();
            for (int tileRow = 0; tileRow < _target.RowCount; tileRow += _block.Rows)
            {
                for (int tileCol = 0; tileCol < _target.ColumnCount; tileCol += _block.Columns)
                {
                    for (int i = 0; i < _block.MergeAnchors.Count; i++)
                    {
                        var (ar, ac) = _block.MergeAnchors[i];
                        var (sr, sc) = _block.MergeSpans[i];
                        int top = _target.Top + tileRow + ar;
                        int left = _target.Left + tileCol + ac;
                        int bottom = top + sr - 1;
                        int right = left + sc - 1;

                        if (top > _target.Bottom || left > _target.Right)
                        {
                            continue; // anchor falls outside the target entirely
                        }

                        bottom = Math.Min(bottom, _target.Bottom);
                        right = Math.Min(right, _target.Right);
                        if (bottom == top && right == left)
                        {
                            continue; // clamped down to a single cell — nothing to merge
                        }

                        var range = new CellRange(new CellAddress(left, top), new CellAddress(right, bottom));
                        if (placed.Any(p => p.Intersects(range)))
                        {
                            continue; // never overlap another freshly placed merge
                        }

                        sheet.AddMerge(new MergedRegion(range));
                        placed.Add(range);
                    }
                }
            }
        }
    }
}
