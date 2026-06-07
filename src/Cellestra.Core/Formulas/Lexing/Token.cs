namespace Cellestra.Core.Formulas.Lexing;

/// <summary>
/// A single lexical token. <see cref="Text"/> carries the literal source slice
/// (decoded for strings), and <see cref="Position"/> is the zero-based offset of the
/// token's start in the formula source — used by <c>ParseError</c> to point at problems.
/// </summary>
public readonly record struct Token(TokenType Type, string Text, int Position)
{
    public override string ToString() => $"{Type}('{Text}')@{Position}";
}
