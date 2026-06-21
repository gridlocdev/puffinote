namespace Cellestra.Core.Formulas.Parsing;

/// <summary>
/// A formula parse problem: a plain-English <see cref="Reason"/> plus the zero-based
/// <see cref="Position"/> in the formula source where it was found (AGENTS "Messages":
/// ParseError carries position + reason; PRD §7 heuristic #9).
/// </summary>
public sealed record ParseError(string Reason, int Position)
{
    public override string ToString() => $"{Reason} (at position {Position})";
}

/// <summary>Thrown by the lexer/parser when a formula cannot be read. Carries a <see cref="ParseError"/>.</summary>
public sealed class FormulaParseException : Exception
{
    public FormulaParseException(ParseError error)
        : base(error.ToString())
    {
        Error = error;
    }

    public FormulaParseException(string reason, int position)
        : this(new ParseError(reason, position))
    {
    }

    public ParseError Error { get; }
}
