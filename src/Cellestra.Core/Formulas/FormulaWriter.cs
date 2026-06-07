using System.Globalization;
using System.Text;
using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas;

/// <summary>
/// Renders a formula AST back to canonical A1 text (without the leading <c>=</c>). Adds the
/// minimum parentheses needed to preserve grouping, so re-emitting after a reference
/// adjustment yields a clean, equivalent formula.
/// </summary>
public static class FormulaWriter
{
    public static string Write(Expr expr) => Render(expr);

    private static string Render(Expr expr) => expr switch
    {
        LiteralExpr literal => RenderLiteral(literal.Value),
        ReferenceExpr reference => reference.Address.ToString(),
        RangeExpr range => range.Range.ToString(),
        NameExpr name => name.Name,
        RefErrorExpr => "#REF!",
        UnaryExpr unary => RenderUnary(unary),
        BinaryExpr binary => RenderBinary(binary),
        FunctionCallExpr call => RenderCall(call),
        _ => throw new InvalidOperationException($"Unknown expression {expr.GetType().Name}."),
    };

    private static string WithParens(Expr expr, int minPrecedence)
    {
        string rendered = Render(expr);
        return Precedence(expr) < minPrecedence ? $"({rendered})" : rendered;
    }

    private static string RenderUnary(UnaryExpr unary) => unary.Operator switch
    {
        UnaryOperator.Negate => "-" + WithParens(unary.Operand, 6),
        UnaryOperator.Plus => "+" + WithParens(unary.Operand, 6),
        UnaryOperator.Percent => WithParens(unary.Operand, 7) + "%",
        _ => Render(unary.Operand),
    };

    private static string RenderBinary(BinaryExpr binary)
    {
        int precedence = Precedence(binary);
        string left = WithParens(binary.Left, precedence);
        string right = WithParens(binary.Right, precedence + 1);
        return $"{left}{Symbol(binary.Operator)}{right}";
    }

    private static string RenderCall(FunctionCallExpr call)
    {
        var sb = new StringBuilder(call.Name);
        sb.Append('(');
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(Render(call.Arguments[i]));
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string RenderLiteral(CellValue value) => value.Type switch
    {
        CellValueType.Number => value.AsNumber.ToString("R", CultureInfo.InvariantCulture),
        CellValueType.Boolean => value.AsBoolean ? "TRUE" : "FALSE",
        CellValueType.Text => $"\"{value.AsText.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
        CellValueType.Error => value.AsError.Token,
        _ => string.Empty,
    };

    private static int Precedence(Expr expr) => expr switch
    {
        BinaryExpr b => b.Operator switch
        {
            BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or
            BinaryOperator.LessEqual or BinaryOperator.Greater or BinaryOperator.GreaterEqual => 1,
            BinaryOperator.Concat => 2,
            BinaryOperator.Add or BinaryOperator.Subtract => 3,
            BinaryOperator.Multiply or BinaryOperator.Divide => 4,
            BinaryOperator.Power => 5,
            _ => 1,
        },
        UnaryExpr u => u.Operator == UnaryOperator.Percent ? 7 : 6,
        _ => 8,
    };

    private static string Symbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Power => "^",
        BinaryOperator.Concat => "&",
        BinaryOperator.Equal => "=",
        BinaryOperator.NotEqual => "<>",
        BinaryOperator.Less => "<",
        BinaryOperator.LessEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterEqual => ">=",
        _ => "?",
    };
}
