using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Lexing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Parsing;

/// <summary>
/// A recursive-descent parser for Cellestra formulas. Operator precedence follows the
/// spreadsheet convention (lowest → highest): comparison, concatenation (<c>&amp;</c>),
/// add/subtract, multiply/divide, power, unary minus, percent, then reference/range.
/// </summary>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _index;

    public Parser(IReadOnlyList<Token> tokens) => _tokens = tokens;

    /// <summary>Parse a formula, with or without its leading <c>=</c>.</summary>
    public static Expr Parse(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        string body = formula.TrimStart();
        if (body.StartsWith('='))
        {
            body = body[1..];
        }

        var tokens = Lexer.Tokenize(body);
        var parser = new Parser(tokens);
        var expr = parser.ParseExpression();
        parser.Expect(TokenType.EndOfInput);
        return expr;
    }

    public static bool TryParse(string formula, out Expr? expr, out ParseError? error)
    {
        try
        {
            expr = Parse(formula);
            error = null;
            return true;
        }
        catch (FormulaParseException ex)
        {
            expr = null;
            error = ex.Error;
            return false;
        }
    }

    private Token Current => _tokens[_index];

    private Token Advance() => _tokens[_index++];

    private bool Match(TokenType type)
    {
        if (Current.Type == type)
        {
            _index++;
            return true;
        }

        return false;
    }

    private Token Expect(TokenType type)
    {
        if (Current.Type != type)
        {
            throw new FormulaParseException($"Expected {Describe(type)} but found {Describe(Current.Type)}.", Current.Position);
        }

        return Advance();
    }

    public Expr ParseExpression() => ParseComparison();

    private Expr ParseComparison()
    {
        var left = ParseConcat();
        while (true)
        {
            BinaryOperator op;
            switch (Current.Type)
            {
                case TokenType.Equal: op = BinaryOperator.Equal; break;
                case TokenType.NotEqual: op = BinaryOperator.NotEqual; break;
                case TokenType.Less: op = BinaryOperator.Less; break;
                case TokenType.LessEqual: op = BinaryOperator.LessEqual; break;
                case TokenType.Greater: op = BinaryOperator.Greater; break;
                case TokenType.GreaterEqual: op = BinaryOperator.GreaterEqual; break;
                default: return left;
            }

            Advance();
            left = new BinaryExpr(op, left, ParseConcat());
        }
    }

    private Expr ParseConcat()
    {
        var left = ParseAddSub();
        while (Current.Type == TokenType.Ampersand)
        {
            Advance();
            left = new BinaryExpr(BinaryOperator.Concat, left, ParseAddSub());
        }

        return left;
    }

    private Expr ParseAddSub()
    {
        var left = ParseMulDiv();
        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            var op = Advance().Type == TokenType.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            left = new BinaryExpr(op, left, ParseMulDiv());
        }

        return left;
    }

    private Expr ParseMulDiv()
    {
        var left = ParseUnary();
        while (Current.Type is TokenType.Star or TokenType.Slash)
        {
            var op = Advance().Type == TokenType.Star ? BinaryOperator.Multiply : BinaryOperator.Divide;
            left = new BinaryExpr(op, left, ParseUnary());
        }

        return left;
    }

    private Expr ParseUnary()
    {
        // Unary +/- binds looser than ^ but tighter than * and / (OpenFormula / LibreOffice
        // Calc order), so -2^2 = -(2^2) = -4. This is the .ods interop target and deliberately
        // differs from Excel, which would give 4.
        if (Current.Type == TokenType.Minus)
        {
            Advance();
            return new UnaryExpr(UnaryOperator.Negate, ParseUnary());
        }

        if (Current.Type == TokenType.Plus)
        {
            Advance();
            return new UnaryExpr(UnaryOperator.Plus, ParseUnary());
        }

        return ParsePower();
    }

    private Expr ParsePower()
    {
        // Left-associative (2^3^2 = (2^3)^2 = 64). A sign directly after ^ (e.g. 2^-3) is a sign
        // on the exponent, binding only to the following postfix — not the whole power chain.
        var left = ParsePostfix();
        while (Current.Type == TokenType.Caret)
        {
            Advance();
            left = new BinaryExpr(BinaryOperator.Power, left, ParsePowerOperand());
        }

        return left;
    }

    private Expr ParsePowerOperand()
    {
        if (Current.Type == TokenType.Minus)
        {
            Advance();
            return new UnaryExpr(UnaryOperator.Negate, ParsePowerOperand());
        }

        if (Current.Type == TokenType.Plus)
        {
            Advance();
            return new UnaryExpr(UnaryOperator.Plus, ParsePowerOperand());
        }

        return ParsePostfix();
    }

    private Expr ParsePostfix()
    {
        var expr = ParsePrimary();
        while (Current.Type == TokenType.Percent)
        {
            Advance();
            expr = new UnaryExpr(UnaryOperator.Percent, expr);
        }

        return expr;
    }

    private Expr ParsePrimary()
    {
        Token token = Current;
        switch (token.Type)
        {
            case TokenType.Number:
                Advance();
                return new LiteralExpr(CellValue.Number(ParseNumber(token)));

            case TokenType.Text:
                Advance();
                return new LiteralExpr(CellValue.Text(token.Text));

            case TokenType.LeftParen:
                Advance();
                var inner = ParseExpression();
                Expect(TokenType.RightParen);
                return inner;

            case TokenType.Name:
                return ParseNameOperand();

            default:
                throw new FormulaParseException($"Unexpected {Describe(token.Type)}.", token.Position);
        }
    }

    private Expr ParseNameOperand()
    {
        Token nameToken = Advance();

        // Function call: NAME( args )
        if (Current.Type == TokenType.LeftParen)
        {
            return ParseFunctionCall(nameToken);
        }

        // Cell reference, possibly the start of a range.
        if (CellAddress.TryParse(nameToken.Text, out var address))
        {
            if (Current.Type == TokenType.Colon)
            {
                Advance();
                return ParseRangeEnd(address, nameToken.Position);
            }

            return new ReferenceExpr(address);
        }

        // Boolean keyword.
        if (string.Equals(nameToken.Text, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return new LiteralExpr(CellValue.Boolean(true));
        }

        if (string.Equals(nameToken.Text, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new LiteralExpr(CellValue.Boolean(false));
        }

        // Otherwise a defined name / named range.
        if (!IsValidName(nameToken.Text))
        {
            throw new FormulaParseException($"'{nameToken.Text}' is not a valid reference or name.", nameToken.Position);
        }

        return new NameExpr(nameToken.Text);
    }

    private Expr ParseRangeEnd(CellAddress start, int position)
    {
        if (Current.Type != TokenType.Name || !CellAddress.TryParse(Current.Text, out var end))
        {
            throw new FormulaParseException("Expected a cell reference after ':'.", Current.Position);
        }

        Advance();

        // A sheet qualifier on the left corner applies to the whole range.
        if (start.Sheet is not null && end.Sheet is null)
        {
            end = end.WithSheet(start.Sheet);
        }

        return new RangeExpr(new CellRange(start, end));
    }

    private Expr ParseFunctionCall(Token nameToken)
    {
        Expect(TokenType.LeftParen);
        var args = new List<Expr>();
        if (Current.Type != TokenType.RightParen)
        {
            args.Add(ParseExpression());
            while (Match(TokenType.Comma))
            {
                args.Add(ParseExpression());
            }
        }

        Expect(TokenType.RightParen);
        return new FunctionCallExpr(nameToken.Text.ToUpperInvariant(), args);
    }

    private static double ParseNumber(Token token)
    {
        if (!double.TryParse(token.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            throw new FormulaParseException($"'{token.Text}' is not a valid number.", token.Position);
        }

        return value;
    }

    private static bool IsValidName(string text)
    {
        // A defined name: starts with a letter or underscore, no embedded dots.
        if (text.Length == 0 || text.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        if (!char.IsLetter(text[0]) && text[0] != '_')
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(TokenType type) => type switch
    {
        TokenType.EndOfInput => "end of formula",
        TokenType.RightParen => "')'",
        TokenType.LeftParen => "'('",
        TokenType.Comma => "','",
        TokenType.Number => "a number",
        TokenType.Text => "a string",
        TokenType.Name => "a name",
        _ => $"'{type}'",
    };
}
