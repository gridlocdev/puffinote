using System.Text;
using Cellestra.Core.Formulas.Parsing;

namespace Cellestra.Core.Formulas.Lexing;

/// <summary>
/// Turns a formula body (the text after the leading <c>=</c>) into a token stream.
/// Numbers always use <c>.</c> as the decimal point and <c>,</c> as the argument
/// separator regardless of locale — locale only affects display, not formula syntax.
/// </summary>
public sealed class Lexer
{
    private readonly string _source;
    private int _pos;

    public Lexer(string source) => _source = source ?? string.Empty;

    public static IReadOnlyList<Token> Tokenize(string source)
    {
        var lexer = new Lexer(source);
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.Next();
            tokens.Add(token);
        }
        while (token.Type != TokenType.EndOfInput);

        return tokens;
    }

    public Token Next()
    {
        SkipWhitespace();
        if (_pos >= _source.Length)
        {
            return new Token(TokenType.EndOfInput, string.Empty, _pos);
        }

        int start = _pos;
        char c = _source[_pos];

        switch (c)
        {
            case '+': return Single(TokenType.Plus);
            case '-': return Single(TokenType.Minus);
            case '*': return Single(TokenType.Star);
            case '/': return Single(TokenType.Slash);
            case '^': return Single(TokenType.Caret);
            case '&': return Single(TokenType.Ampersand);
            case '%': return Single(TokenType.Percent);
            case '(': return Single(TokenType.LeftParen);
            case ')': return Single(TokenType.RightParen);
            case ',': return Single(TokenType.Comma);
            case ':': return Single(TokenType.Colon);
            case '=': return Single(TokenType.Equal);
            case '<': return LessOrCompound();
            case '>': return GreaterOrCompound();
            case '"': return ReadString();
        }

        if (char.IsAsciiDigit(c) || (c == '.' && _pos + 1 < _source.Length && char.IsAsciiDigit(_source[_pos + 1])))
        {
            return ReadNumber();
        }

        if (char.IsLetter(c) || c is '_' or '$' or '\'')
        {
            return ReadName();
        }

        throw new FormulaParseException($"Unexpected character '{c}'.", start);
    }

    private Token Single(TokenType type)
    {
        int start = _pos;
        char c = _source[_pos++];
        return new Token(type, c.ToString(), start);
    }

    private Token LessOrCompound()
    {
        int start = _pos++;
        if (_pos < _source.Length && _source[_pos] == '=')
        {
            _pos++;
            return new Token(TokenType.LessEqual, "<=", start);
        }

        if (_pos < _source.Length && _source[_pos] == '>')
        {
            _pos++;
            return new Token(TokenType.NotEqual, "<>", start);
        }

        return new Token(TokenType.Less, "<", start);
    }

    private Token GreaterOrCompound()
    {
        int start = _pos++;
        if (_pos < _source.Length && _source[_pos] == '=')
        {
            _pos++;
            return new Token(TokenType.GreaterEqual, ">=", start);
        }

        return new Token(TokenType.Greater, ">", start);
    }

    private Token ReadString()
    {
        int start = _pos;
        _pos++; // opening quote
        var sb = new StringBuilder();
        while (_pos < _source.Length)
        {
            char c = _source[_pos];
            if (c == '"')
            {
                // A doubled quote ("") is an escaped quote character.
                if (_pos + 1 < _source.Length && _source[_pos + 1] == '"')
                {
                    sb.Append('"');
                    _pos += 2;
                    continue;
                }

                _pos++; // closing quote
                return new Token(TokenType.Text, sb.ToString(), start);
            }

            sb.Append(c);
            _pos++;
        }

        throw new FormulaParseException("Unterminated string literal.", start);
    }

    private Token ReadNumber()
    {
        int start = _pos;
        while (_pos < _source.Length && char.IsAsciiDigit(_source[_pos]))
        {
            _pos++;
        }

        if (_pos < _source.Length && _source[_pos] == '.')
        {
            _pos++;
            while (_pos < _source.Length && char.IsAsciiDigit(_source[_pos]))
            {
                _pos++;
            }
        }

        if (_pos < _source.Length && (_source[_pos] == 'e' || _source[_pos] == 'E'))
        {
            int expStart = _pos;
            _pos++;
            if (_pos < _source.Length && (_source[_pos] == '+' || _source[_pos] == '-'))
            {
                _pos++;
            }

            if (_pos >= _source.Length || !char.IsAsciiDigit(_source[_pos]))
            {
                // Not actually an exponent (e.g. trailing 'E'); rewind.
                _pos = expStart;
            }
            else
            {
                while (_pos < _source.Length && char.IsAsciiDigit(_source[_pos]))
                {
                    _pos++;
                }
            }
        }

        return new Token(TokenType.Number, _source[start.._pos], start);
    }

    private Token ReadName()
    {
        int start = _pos;

        if (_source[_pos] == '\'')
        {
            ReadQuotedSheetPrefix(start);
        }

        while (_pos < _source.Length && IsNameChar(_source[_pos]))
        {
            _pos++;
        }

        if (_pos == start)
        {
            throw new FormulaParseException("Invalid name.", start);
        }

        return new Token(TokenType.Name, _source[start.._pos], start);
    }

    private void ReadQuotedSheetPrefix(int start)
    {
        _pos++; // opening quote
        while (_pos < _source.Length)
        {
            char c = _source[_pos];
            if (c == '\'')
            {
                if (_pos + 1 < _source.Length && _source[_pos + 1] == '\'')
                {
                    _pos += 2; // escaped quote inside the sheet name
                    continue;
                }

                _pos++; // closing quote
                if (_pos >= _source.Length || _source[_pos] != '.')
                {
                    throw new FormulaParseException("A quoted sheet name must be followed by '.cell'.", start);
                }

                _pos++; // the dot; the A1 part is read by the caller's name loop
                return;
            }

            _pos++;
        }

        throw new FormulaParseException("Unterminated sheet name.", start);
    }

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '$' or '.';

    private void SkipWhitespace()
    {
        while (_pos < _source.Length && char.IsWhiteSpace(_source[_pos]))
        {
            _pos++;
        }
    }
}
