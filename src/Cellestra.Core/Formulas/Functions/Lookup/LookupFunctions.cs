using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions.Lookup;

/// <summary>The v1 lookup &amp; reference functions (PRD §8.2).</summary>
public static class LookupFunctions
{
    public static IEnumerable<IFunction> All()
    {
        yield return new DelegateFunction(
            H.Meta("VLOOKUP", FunctionCategory.Lookup, "Looks up a value in the first column of a table and returns a value in the same row.",
                ArgumentKind.Any, "VLOOKUP(A1, D1:F20, 3, FALSE)",
                H.Val("lookup"), H.Rng("table"), H.Num("column"), H.Bln("approximate", required: false)),
            (args, ev) => LookupTable(args, ev, vertical: true));

        yield return new DelegateFunction(
            H.Meta("HLOOKUP", FunctionCategory.Lookup, "Looks up a value in the first row of a table and returns a value in the same column.",
                ArgumentKind.Any, "HLOOKUP(A1, D1:Z2, 2, FALSE)",
                H.Val("lookup"), H.Rng("table"), H.Num("row"), H.Bln("approximate", required: false)),
            (args, ev) => LookupTable(args, ev, vertical: false));

        yield return new DelegateFunction(
            H.Meta("MATCH", FunctionCategory.Lookup, "Returns the position of a value within a row or column.",
                ArgumentKind.Number, "MATCH(A1, D1:D20, 0)",
                H.Val("lookup"), H.Rng("range"), H.Num("match_type", required: false)),
            (args, ev) =>
            {
                var lookup = ev.Evaluate(args[0]);
                if (lookup.IsError)
                {
                    return lookup;
                }

                if (!ev.TryEvaluateRange(args[1], out var range))
                {
                    return CellValue.FromError(CellError.Value("MATCH needs a range to search."));
                }

                int matchType = 1;
                if (args.Count > 2 && !H.TryInt(ev, args[2], out matchType, out var em))
                {
                    return em;
                }

                var values = Flatten1D(range, out var shapeError);
                if (shapeError is not null)
                {
                    return shapeError.Value;
                }

                int index = Search(values, lookup, matchType, ev.Context.Culture, out var searchError);
                if (searchError is not null)
                {
                    return searchError.Value;
                }

                return index < 0
                    ? CellValue.FromError(CellError.NotAvailable("No match found."))
                    : CellValue.Number(index + 1);
            });

        yield return new DelegateFunction(
            H.Meta("INDEX", FunctionCategory.Lookup, "Returns the value at a position within a range.",
                ArgumentKind.Any, "INDEX(A1:C10, 2, 3)",
                H.Rng("range"), H.Num("row"), H.Num("column", required: false)),
            (args, ev) =>
            {
                if (!ev.TryEvaluateRange(args[0], out var range))
                {
                    return CellValue.FromError(CellError.Value("INDEX needs a range."));
                }

                if (!H.TryInt(ev, args[1], out int rowArg, out var er))
                {
                    return er;
                }

                int? colArg = null;
                if (args.Count > 2)
                {
                    if (!H.TryInt(ev, args[2], out int parsed, out var ec))
                    {
                        return ec;
                    }

                    colArg = parsed;
                }

                return Index(range, rowArg, colArg);
            });
    }

    private static CellValue LookupTable(IReadOnlyList<Expr> args, IFunctionEvaluator ev, bool vertical)
    {
        var lookup = ev.Evaluate(args[0]);
        if (lookup.IsError)
        {
            return lookup;
        }

        if (!ev.TryEvaluateRange(args[1], out var table))
        {
            return CellValue.FromError(CellError.Value("The lookup table must be a range."));
        }

        if (!H.TryInt(ev, args[2], out int index, out var ei))
        {
            return ei;
        }

        bool approximate = true;
        if (args.Count > 3 && !H.TryBool(ev, args[3], out approximate, out var ea))
        {
            return ea;
        }

        // The first row/column is the search vector.
        int length = vertical ? table.RowCount : table.ColumnCount;
        var vector = new CellValue[length];
        for (int i = 0; i < length; i++)
        {
            vector[i] = vertical ? table.Get(i, 0) : table.Get(0, i);
        }

        int matchType = approximate ? 1 : 0;
        int position = Search(vector, lookup, matchType, ev.Context.Culture, out var searchError);
        if (searchError is not null)
        {
            return searchError.Value;
        }

        if (position < 0)
        {
            return CellValue.FromError(CellError.NotAvailable("No match found."));
        }

        int otherCount = vertical ? table.ColumnCount : table.RowCount;
        if (index < 1 || index > otherCount)
        {
            return CellValue.FromError(CellError.Reference($"Index {index} is outside the table."));
        }

        return vertical ? table.Get(position, index - 1) : table.Get(index - 1, position);
    }

    private static CellValue Index(RangeValues range, int rowArg, int? colArg)
    {
        int rowWithin;
        int colWithin;

        if (colArg is int col)
        {
            rowWithin = rowArg - 1;
            colWithin = col - 1;
        }
        else if (range.RowCount == 1)
        {
            rowWithin = 0;
            colWithin = rowArg - 1;
        }
        else if (range.ColumnCount == 1)
        {
            rowWithin = rowArg - 1;
            colWithin = 0;
        }
        else
        {
            return CellValue.FromError(CellError.Reference("INDEX needs a column number for a 2D range."));
        }

        if (rowWithin < 0 || rowWithin >= range.RowCount || colWithin < 0 || colWithin >= range.ColumnCount)
        {
            return CellValue.FromError(CellError.Reference("INDEX position is outside the range."));
        }

        return range.Get(rowWithin, colWithin);
    }

    private static CellValue[] Flatten1D(RangeValues range, out CellValue? error)
    {
        error = null;
        if (range.RowCount != 1 && range.ColumnCount != 1)
        {
            error = CellValue.FromError(CellError.NotAvailable("MATCH needs a single row or column."));
            return Array.Empty<CellValue>();
        }

        return range.Values().ToArray();
    }

    private static int Search(CellValue[] values, CellValue lookup, int matchType, System.Globalization.CultureInfo culture, out CellValue? error)
    {
        error = null;

        if (matchType == 0)
        {
            // Exact match, honoring text wildcards.
            var matcher = lookup.IsText ? CriteriaMatcher.Build(lookup, culture) : null;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].IsError)
                {
                    error = values[i];
                    return -1;
                }

                bool isMatch = matcher is not null
                    ? matcher.Matches(values[i])
                    : EqualsValue(values[i], lookup);
                if (isMatch)
                {
                    return i;
                }
            }

            return -1;
        }

        // Approximate: assume sorted (ascending for 1, descending for -1); take the last
        // value on the correct side of the lookup.
        int best = -1;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].IsError)
            {
                error = values[i];
                return -1;
            }

            int cmp = ValueComparer.Compare(values[i], lookup, out var cmpError);
            if (cmpError is not null)
            {
                error = CellValue.FromError(cmpError);
                return -1;
            }

            bool onSide = matchType > 0 ? cmp <= 0 : cmp >= 0;
            if (onSide)
            {
                best = i;
            }
            else
            {
                break; // sorted assumption: once we pass the boundary, stop
            }
        }

        return best;
    }

    private static bool EqualsValue(CellValue a, CellValue b)
    {
        int cmp = ValueComparer.Compare(a, b, out var error);
        return error is null && cmp == 0;
    }
}
