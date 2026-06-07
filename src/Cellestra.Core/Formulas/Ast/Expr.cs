using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Ast;

public enum UnaryOperator
{
    Negate,
    Plus,
    Percent,
}

public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,
    Concat,
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
}

/// <summary>A parsed formula expression node. Walk it with an <see cref="IExprVisitor{T}"/>.</summary>
public abstract record Expr
{
    public abstract T Accept<T>(IExprVisitor<T> visitor);
}

/// <summary>A literal number, string, or boolean.</summary>
public sealed record LiteralExpr(CellValue Value) : Expr
{
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitLiteral(this);
}

/// <summary>A single cell reference such as <c>A1</c>, <c>$A$1</c>, or <c>Sheet2.A1</c>.</summary>
public sealed record ReferenceExpr(CellAddress Address) : Expr
{
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitReference(this);
}

/// <summary>A rectangular range reference such as <c>A1:B3</c>.</summary>
public sealed record RangeExpr(CellRange Range) : Expr
{
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitRange(this);
}

/// <summary>A defined name / named range used in a formula (resolved at evaluation).</summary>
public sealed record NameExpr(string Name) : Expr
{
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitName(this);
}

public sealed record UnaryExpr(UnaryOperator Operator, Expr Operand) : Expr
{
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitUnary(this);
}

public sealed record BinaryExpr(BinaryOperator Operator, Expr Left, Expr Right) : Expr
{
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitBinary(this);
}

public sealed record FunctionCallExpr(string Name, IReadOnlyList<Expr> Arguments) : Expr
{
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitFunctionCall(this);
}

/// <summary>Visitor over the formula AST. Implemented by the evaluator and by reference collectors.</summary>
public interface IExprVisitor<out T>
{
    T VisitLiteral(LiteralExpr node);

    T VisitReference(ReferenceExpr node);

    T VisitRange(RangeExpr node);

    T VisitName(NameExpr node);

    T VisitUnary(UnaryExpr node);

    T VisitBinary(BinaryExpr node);

    T VisitFunctionCall(FunctionCallExpr node);
}
