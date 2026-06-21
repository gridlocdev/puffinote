using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Evaluation;

/// <summary>
/// Ordering rules for the comparison operators (= &lt;&gt; &lt; &lt;= &gt; &gt;=). Within a
/// kind, numbers compare numerically and text compares case-insensitively. Across kinds the
/// rank is numbers &lt; text &lt; booleans (the spreadsheet convention). A blank cell takes the
/// shape of whatever it is compared against (0, "", or FALSE). Errors propagate.
/// </summary>
public static class ValueComparer
{
    /// <summary>Compare two values. Sets <paramref name="error"/> and returns 0 if either is an error.</summary>
    public static int Compare(CellValue left, CellValue right, out CellError? error)
    {
        error = null;
        if (left.IsError)
        {
            error = left.AsError;
            return 0;
        }

        if (right.IsError)
        {
            error = right.AsError;
            return 0;
        }

        // Normalize a blank to the kind of the other operand.
        (left, right) = NormalizeBlanks(left, right);

        int leftRank = Rank(left);
        int rightRank = Rank(right);
        if (leftRank != rightRank)
        {
            return leftRank.CompareTo(rightRank);
        }

        return leftRank switch
        {
            RankText => string.Compare(left.AsText, right.AsText, StringComparison.OrdinalIgnoreCase),
            _ => left.AsNumber.CompareTo(right.AsNumber), // numbers and booleans both numeric within a rank
        };
    }

    public static CellValue Equal(CellValue a, CellValue b) => Relational(a, b, c => c == 0);

    public static CellValue NotEqual(CellValue a, CellValue b) => Relational(a, b, c => c != 0);

    public static CellValue Less(CellValue a, CellValue b) => Relational(a, b, c => c < 0);

    public static CellValue LessEqual(CellValue a, CellValue b) => Relational(a, b, c => c <= 0);

    public static CellValue Greater(CellValue a, CellValue b) => Relational(a, b, c => c > 0);

    public static CellValue GreaterEqual(CellValue a, CellValue b) => Relational(a, b, c => c >= 0);

    private static CellValue Relational(CellValue a, CellValue b, Func<int, bool> predicate)
    {
        int cmp = Compare(a, b, out var error);
        return error is not null ? CellValue.FromError(error) : CellValue.Boolean(predicate(cmp));
    }

    private const int RankNumber = 0;
    private const int RankText = 1;
    private const int RankBoolean = 2;

    private static int Rank(CellValue value) => value.Type switch
    {
        CellValueType.Boolean => RankBoolean,
        CellValueType.Text => RankText,
        _ => RankNumber, // Number and (normalized) Empty
    };

    private static (CellValue, CellValue) NormalizeBlanks(CellValue left, CellValue right)
    {
        if (left.IsEmpty && !right.IsEmpty)
        {
            left = BlankAs(right);
        }
        else if (right.IsEmpty && !left.IsEmpty)
        {
            right = BlankAs(left);
        }
        else if (left.IsEmpty && right.IsEmpty)
        {
            left = right = CellValue.Number(0);
        }

        return (left, right);
    }

    private static CellValue BlankAs(CellValue other) => other.Type switch
    {
        CellValueType.Text => CellValue.Text(string.Empty),
        CellValueType.Boolean => CellValue.Boolean(false),
        _ => CellValue.Number(0),
    };
}
