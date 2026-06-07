namespace Cellestra.Core.Values;

/// <summary>The canonical spreadsheet error codes Cellestra surfaces (PRD §8, Formulas).</summary>
public enum CellErrorCode
{
    /// <summary>#DIV/0! — division by zero (or an empty/zero divisor).</summary>
    DivideByZero,

    /// <summary>#REF! — a reference to a cell that no longer exists, or a circular reference.</summary>
    Reference,

    /// <summary>#VALUE! — a value of the wrong type for the operation.</summary>
    Value,

    /// <summary>#NAME? — an unknown name (function, named range, or mistyped reference).</summary>
    Name,

    /// <summary>#N/A — a value is not available (e.g. a failed lookup).</summary>
    NotAvailable,
}

/// <summary>
/// An error value carried by a cell or an evaluation. Pairs a canonical
/// <see cref="CellErrorCode"/> (for the <c>#…!</c> token shown in the cell) with a
/// plain-English <see cref="Reason"/> shown on hover (PRD §7 heuristic #9; AGENTS "Messages").
/// </summary>
public sealed record CellError(CellErrorCode Code, string Reason)
{
    /// <summary>The short token displayed in the cell, e.g. <c>#DIV/0!</c>.</summary>
    public string Token => Code switch
    {
        CellErrorCode.DivideByZero => "#DIV/0!",
        CellErrorCode.Reference => "#REF!",
        CellErrorCode.Value => "#VALUE!",
        CellErrorCode.Name => "#NAME?",
        CellErrorCode.NotAvailable => "#N/A",
        _ => "#ERR",
    };

    public static CellError DivideByZero(string reason = "Division by zero.") =>
        new(CellErrorCode.DivideByZero, reason);

    public static CellError Reference(string reason = "Invalid cell reference.") =>
        new(CellErrorCode.Reference, reason);

    /// <summary>A circular reference surfaces as #REF! with an explanatory reason.</summary>
    public static CellError Circular(string reason = "Circular reference.") =>
        new(CellErrorCode.Reference, reason);

    public static CellError Value(string reason = "A value is of the wrong type.") =>
        new(CellErrorCode.Value, reason);

    public static CellError Name(string reason = "Unknown name.") =>
        new(CellErrorCode.Name, reason);

    public static CellError NotAvailable(string reason = "Value not available.") =>
        new(CellErrorCode.NotAvailable, reason);

    public override string ToString() => Token;
}
