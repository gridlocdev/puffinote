using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions;

/// <summary>
/// A read-only 2D view over a resolved range, fetched lazily from the evaluation context
/// so a large range is never fully materialized. Coordinates are zero-based <b>within</b>
/// the range. Used by INDEX/MATCH/VLOOKUP/HLOOKUP and by value aggregation.
/// </summary>
public sealed class RangeValues
{
    private readonly IEvaluationContext _context;
    private readonly string? _sheet;
    private readonly CellRange _range;

    public RangeValues(CellRange range, IEvaluationContext context)
    {
        _range = range;
        _context = context;
        _sheet = range.Sheet ?? context.CurrentSheet;
    }

    public int RowCount => _range.RowCount;

    public int ColumnCount => _range.ColumnCount;

    public CellRange Range => _range;

    public CellValue Get(int rowWithin, int columnWithin)
    {
        if (rowWithin < 0 || rowWithin >= RowCount || columnWithin < 0 || columnWithin >= ColumnCount)
        {
            return CellValue.FromError(CellError.Reference("Index outside the range."));
        }

        return _context.GetCell(_sheet, _range.Top + rowWithin, _range.Left + columnWithin);
    }

    /// <summary>Every value in the range, row-major.</summary>
    public IEnumerable<CellValue> Values()
    {
        for (int r = 0; r < RowCount; r++)
        {
            for (int c = 0; c < ColumnCount; c++)
            {
                yield return Get(r, c);
            }
        }
    }
}
