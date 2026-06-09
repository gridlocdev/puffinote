using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Model;

namespace Cellestra.Core.Formulas.References;

/// <summary>
/// Rewrites the references inside a formula AST — the one place reference fix-ups live
/// (Golden Rule #8). On <b>copy/fill</b>, relative refs shift by the offset and absolute
/// (<c>$</c>) refs stay put. On <b>insert/delete row/column</b>, refs move with their target
/// and refs into deleted cells become <c>#REF!</c>. The caller serializes the result with
/// <see cref="FormulaWriter"/>.
/// </summary>
public static class ReferenceAdjuster
{
    /// <summary>Adjust for a copy/fill from one position to another: shift relative parts by the delta.</summary>
    public static Expr ForCopy(Expr expr, int columnDelta, int rowDelta) =>
        Rewrite(expr,
            address => ShiftAddress(address, columnDelta, rowDelta),
            range => ShiftRange(range, columnDelta, rowDelta));

    public static Expr ForInsertRows(Expr expr, string homeSheet, string editedSheet, int atRow, int count) =>
        AxisRewrite(expr, homeSheet, editedSheet, rowAxis: true, AxisEdit.Insert(atRow, count, SheetDimensions.MaxRows));

    public static Expr ForDeleteRows(Expr expr, string homeSheet, string editedSheet, int atRow, int count) =>
        AxisRewrite(expr, homeSheet, editedSheet, rowAxis: true, AxisEdit.Delete(atRow, count));

    public static Expr ForInsertColumns(Expr expr, string homeSheet, string editedSheet, int atColumn, int count) =>
        AxisRewrite(expr, homeSheet, editedSheet, rowAxis: false, AxisEdit.Insert(atColumn, count, SheetDimensions.MaxColumns));

    public static Expr ForDeleteColumns(Expr expr, string homeSheet, string editedSheet, int atColumn, int count) =>
        AxisRewrite(expr, homeSheet, editedSheet, rowAxis: false, AxisEdit.Delete(atColumn, count));

    /// <summary>
    /// Adjust references when a row moves during a sort (PRD §8.11). A relative reference into the
    /// sorted data rows shifts by the same delta the formula's own row moved, so per-row formulas
    /// (e.g. <c>=B2*C2</c>) keep pointing at their row's data. Absolute (<c>$</c>) refs, cross-sheet
    /// refs, and refs outside the sorted rows stay put.
    /// </summary>
    public static Expr ForSortMove(Expr expr, int rowDelta, int dataTop, int dataBottom)
    {
        CellAddress MapAddress(CellAddress address)
        {
            if (address.Sheet is not null || address.RowAbsolute || address.Row < dataTop || address.Row > dataBottom)
            {
                return address;
            }

            int newRow = address.Row + rowDelta;
            return SheetDimensions.IsValidRow(newRow)
                ? new CellAddress(address.Column, newRow, address.ColumnAbsolute, address.RowAbsolute, address.Sheet)
                : address;
        }

        CellRange MapRange(CellRange range) => new(MapAddress(range.Start), MapAddress(range.End));

        return Rewrite(expr, a => MapAddress(a), r => MapRange(r));
    }

    /// <summary>
    /// Adjust references when a worksheet is deleted: any reference whose effective sheet is the
    /// deleted one becomes <c>#REF!</c>. A reference with no explicit sheet belongs to
    /// <paramref name="homeSheet"/> (the formula's own sheet) and is only affected if that is the
    /// sheet being deleted — which won't matter, since such a formula is deleted along with it.
    /// </summary>
    public static Expr ForDeleteSheet(Expr expr, string deletedSheet, string homeSheet)
    {
        bool Affected(string? sheet) =>
            string.Equals(sheet ?? homeSheet, deletedSheet, StringComparison.OrdinalIgnoreCase);

        return Rewrite(expr,
            address => Affected(address.Sheet) ? null : address,
            range => Affected(range.Sheet) ? null : range);
    }

    // ---- Generic AST rewrite ---------------------------------------------

    private static Expr Rewrite(Expr expr, Func<CellAddress, CellAddress?> mapAddress, Func<CellRange, CellRange?> mapRange) =>
        expr switch
        {
            ReferenceExpr reference => mapAddress(reference.Address) is { } mapped
                ? new ReferenceExpr(mapped)
                : RefErrorExpr.Instance,
            RangeExpr range => mapRange(range.Range) is { } mapped
                ? new RangeExpr(mapped)
                : RefErrorExpr.Instance,
            UnaryExpr unary => new UnaryExpr(unary.Operator, Rewrite(unary.Operand, mapAddress, mapRange)),
            BinaryExpr binary => new BinaryExpr(binary.Operator,
                Rewrite(binary.Left, mapAddress, mapRange),
                Rewrite(binary.Right, mapAddress, mapRange)),
            FunctionCallExpr call => new FunctionCallExpr(call.Name,
                call.Arguments.Select(a => Rewrite(a, mapAddress, mapRange)).ToList()),
            _ => expr, // literals, names, ref-errors are unchanged
        };

    private static Expr AxisRewrite(Expr expr, string homeSheet, string editedSheet, bool rowAxis, AxisEdit edit)
    {
        CellAddress? MapAddress(CellAddress address)
        {
            if (!AffectsSheet(address.Sheet, homeSheet, editedSheet))
            {
                return address;
            }

            int coord = rowAxis ? address.Row : address.Column;
            int? mapped = edit.Map(coord);
            return mapped is { } value ? WithCoord(address, rowAxis, value) : null;
        }

        CellRange? MapRange(CellRange range)
        {
            if (!AffectsSheet(range.Sheet, homeSheet, editedSheet))
            {
                return range;
            }

            // Work on the normalized span on the edited axis; the whole range is #REF! only
            // when every line of it is deleted.
            int min = rowAxis ? range.Top : range.Left;
            int max = rowAxis ? range.Bottom : range.Right;
            if (edit.SurvivingExtremes(min, max) is not (int survivingMin, int survivingMax))
            {
                return null;
            }

            int newMin = edit.Map(survivingMin)!.Value;
            int newMax = edit.Map(survivingMax)!.Value;

            // Re-attach each original corner's $ flags: the corner at the span's min keeps newMin.
            CellAddress NewCorner(CellAddress corner)
            {
                int coord = rowAxis ? corner.Row : corner.Column;
                int value = coord == min ? newMin : newMax;
                return WithCoord(corner, rowAxis, value);
            }

            return new CellRange(NewCorner(range.Start), NewCorner(range.End));
        }

        return Rewrite(expr, MapAddress, MapRange);
    }

    private static bool AffectsSheet(string? referenceSheet, string homeSheet, string editedSheet)
    {
        string effective = referenceSheet ?? homeSheet;
        return string.Equals(effective, editedSheet, StringComparison.OrdinalIgnoreCase);
    }

    private static CellAddress WithCoord(CellAddress address, bool rowAxis, int value) => rowAxis
        ? new CellAddress(address.Column, value, address.ColumnAbsolute, address.RowAbsolute, address.Sheet)
        : new CellAddress(value, address.Row, address.ColumnAbsolute, address.RowAbsolute, address.Sheet);

    private static CellAddress? ShiftAddress(CellAddress address, int columnDelta, int rowDelta)
    {
        int column = address.ColumnAbsolute ? address.Column : address.Column + columnDelta;
        int row = address.RowAbsolute ? address.Row : address.Row + rowDelta;
        if (!SheetDimensions.IsValidColumn(column) || !SheetDimensions.IsValidRow(row))
        {
            return null;
        }

        return new CellAddress(column, row, address.ColumnAbsolute, address.RowAbsolute, address.Sheet);
    }

    private static CellRange? ShiftRange(CellRange range, int columnDelta, int rowDelta)
    {
        var start = ShiftAddress(range.Start, columnDelta, rowDelta);
        var end = ShiftAddress(range.End, columnDelta, rowDelta);
        return start is { } s && end is { } e ? new CellRange(s, e) : null;
    }

    /// <summary>
    /// A one-axis insert or delete, mapping old line indices to new ones. Insert(at, n)
    /// pushes lines at/after <c>at</c> down by <c>n</c>; Delete(at, n) removes lines
    /// <c>at … at+n-1</c> (those map to "gone") and pulls later lines up by <c>n</c>.
    /// </summary>
    private sealed class AxisEdit
    {
        private readonly bool _insert;
        private readonly int _at;
        private readonly int _count;
        private readonly int _limit; // exclusive upper bound for insert clamping

        private AxisEdit(bool insert, int at, int count, int limit)
        {
            _insert = insert;
            _at = at;
            _count = count;
            _limit = limit;
        }

        public static AxisEdit Insert(int at, int count, int limit) => new(true, at, count, limit);

        public static AxisEdit Delete(int at, int count) => new(false, at, count, int.MaxValue);

        /// <summary>Map an old index to its new one; null means the line was deleted.</summary>
        public int? Map(int coord)
        {
            if (_insert)
            {
                int shifted = coord >= _at ? coord + _count : coord;

                // An insert that pushes a reference past the sheet's last row/column has nowhere
                // to land — it becomes #REF! (null) rather than silently clamping to the edge.
                return shifted >= _limit ? null : shifted;
            }

            int deleteEnd = _at + _count;
            if (coord < _at)
            {
                return coord;
            }

            return coord < deleteEnd ? null : coord - _count;
        }

        /// <summary>The smallest and largest old indices in [from, to] that survive, or null if none do.</summary>
        public (int Min, int Max)? SurvivingExtremes(int from, int to)
        {
            if (_insert)
            {
                return (from, to);
            }

            int deleteEnd = _at + _count;
            int? min = from < _at ? from : (System.Math.Max(from, deleteEnd) <= to ? System.Math.Max(from, deleteEnd) : null);
            int? max = to >= deleteEnd ? to : (to < _at ? to : (_at - 1 >= from ? _at - 1 : null));
            return min is { } lo && max is { } hi ? (lo, hi) : null;
        }
    }
}
