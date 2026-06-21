namespace Cellestra.Core.Formulas.Lexing;

/// <summary>The kinds of lexical token in a Cellestra formula (user-facing A1 syntax).</summary>
public enum TokenType
{
    Number,

    /// <summary>A double-quoted string literal; embedded quotes are doubled (<c>""</c>).</summary>
    Text,

    /// <summary>
    /// A name operand: a function name, a named range, a boolean keyword, or a cell
    /// reference (possibly sheet-qualified, with <c>$</c> flags). The parser classifies it.
    /// </summary>
    Name,

    Plus,
    Minus,
    Star,
    Slash,
    Caret,
    Ampersand,
    Percent,

    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,

    LeftParen,
    RightParen,
    Comma,

    /// <summary>The range operator <c>:</c>.</summary>
    Colon,

    EndOfInput,
}
