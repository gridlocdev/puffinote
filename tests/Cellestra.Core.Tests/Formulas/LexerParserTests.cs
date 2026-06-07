using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Lexing;
using Cellestra.Core.Formulas.Parsing;
using Xunit;

namespace Cellestra.Core.Tests.Formulas;

public class LexerParserTests
{
    [Fact]
    public void Lexer_tokenizes_a_function_call()
    {
        var tokens = Lexer.Tokenize("SUM(A1:A3, 5)");

        Assert.Equal(
            new[]
            {
                TokenType.Name, TokenType.LeftParen, TokenType.Name, TokenType.Colon, TokenType.Name,
                TokenType.Comma, TokenType.Number, TokenType.RightParen, TokenType.EndOfInput,
            },
            tokens.Select(t => t.Type).ToArray());
    }

    [Theory]
    [InlineData("<=", TokenType.LessEqual)]
    [InlineData(">=", TokenType.GreaterEqual)]
    [InlineData("<>", TokenType.NotEqual)]
    public void Lexer_reads_compound_operators(string source, TokenType expected)
    {
        Assert.Equal(expected, Lexer.Tokenize(source)[0].Type);
    }

    [Fact]
    public void Lexer_decodes_doubled_quotes_in_strings()
    {
        var token = Lexer.Tokenize("\"a\"\"b\"")[0];

        Assert.Equal(TokenType.Text, token.Type);
        Assert.Equal("a\"b", token.Text);
    }

    [Fact]
    public void Lexer_keeps_quoted_sheet_name_with_reference()
    {
        var token = Lexer.Tokenize("'My Sheet'.A1")[0];

        Assert.Equal(TokenType.Name, token.Type);
        Assert.Equal("'My Sheet'.A1", token.Text);
    }

    [Fact]
    public void Parser_strips_leading_equals()
    {
        var expr = Parser.Parse("=1+2");
        Assert.IsType<BinaryExpr>(expr);
    }

    [Fact]
    public void Parser_respects_precedence()
    {
        // 1 + 2 * 3 → 1 + (2 * 3)
        var expr = Assert.IsType<BinaryExpr>(Parser.Parse("1 + 2 * 3"));
        Assert.Equal(BinaryOperator.Add, expr.Operator);
        var right = Assert.IsType<BinaryExpr>(expr.Right);
        Assert.Equal(BinaryOperator.Multiply, right.Operator);
    }

    [Fact]
    public void Parser_classifies_name_kinds()
    {
        Assert.IsType<ReferenceExpr>(Parser.Parse("A1"));
        Assert.IsType<RangeExpr>(Parser.Parse("A1:B2"));
        Assert.IsType<NameExpr>(Parser.Parse("TaxRate"));
        Assert.IsType<FunctionCallExpr>(Parser.Parse("SUM(A1)"));
        Assert.IsType<LiteralExpr>(Parser.Parse("TRUE"));
    }

    [Fact]
    public void Parser_reports_position_on_error()
    {
        Assert.False(Parser.TryParse("1 + ", out _, out var error));
        Assert.NotNull(error);
        Assert.Equal(4, error!.Position);
    }

    [Fact]
    public void Parser_rejects_unbalanced_parens()
    {
        Assert.False(Parser.TryParse("SUM(A1", out _, out _));
    }
}
