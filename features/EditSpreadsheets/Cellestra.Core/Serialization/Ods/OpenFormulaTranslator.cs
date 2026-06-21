using System.Globalization;
using System.Text;
using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>
/// Converts between Cellestra's internal A1 formula text and ODF <b>OpenFormula</b>
/// (Golden Rule #7): references are bracketed with a sheet-dot (<c>[.A1]</c>, <c>[Sheet2.A1]</c>),
/// arguments are separated by <c>;</c>, and the whole thing is prefixed <c>of:=</c>. Always go
/// through here when reading/writing <c>.ods</c> formulas — typed text corrupts on round-trip.
/// </summary>
public static class OpenFormulaTranslator
{
    private const string Prefix = "of:=";

    /// <summary>Internal A1 formula (no leading <c>=</c>) → an OpenFormula string with the <c>of:=</c> prefix.</summary>
    public static string ToOpenFormula(string internalFormula)
    {
        ArgumentNullException.ThrowIfNull(internalFormula);
        if (!Parser.TryParse(internalFormula, out var expr, out _) || expr is null)
        {
            // Unparseable formula: still emit something that preserves the text.
            return Prefix + internalFormula;
        }

        return Prefix + Render(expr);
    }

    /// <summary>OpenFormula string → internal A1 formula (no leading <c>=</c>).</summary>
    public static string FromOpenFormula(string openFormula)
    {
        ArgumentNullException.ThrowIfNull(openFormula);
        string body = StripPrefix(openFormula);
        string internalText = ConvertReferencesAndSeparators(body);

        // Normalize through the parser when possible; keep the converted text otherwise so
        // formulas using unmodeled functions still survive the round-trip.
        return Parser.TryParse(internalText, out var expr, out _) && expr is not null
            ? Cellestra.Core.Formulas.FormulaWriter.Write(expr)
            : internalText;
    }

    // ---- A1 AST → OpenFormula --------------------------------------------

    private static string Render(Expr expr) => expr switch
    {
        LiteralExpr literal => RenderLiteral(literal.Value),
        ReferenceExpr reference => RenderAddress(reference.Address),
        RangeExpr range => RenderRange(range.Range),
        NameExpr name => name.Name,
        RefErrorExpr => "[#REF!]",
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
        return $"{WithParens(binary.Left, precedence)}{Symbol(binary.Operator)}{WithParens(binary.Right, precedence + 1)}";
    }

    private static string RenderCall(FunctionCallExpr call)
    {
        var sb = new StringBuilder(call.Name);
        sb.Append('(');
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';'); // OpenFormula argument separator
            }

            sb.Append(Render(call.Arguments[i]));
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string RenderLiteral(CellValue value) => value.Type switch
    {
        CellValueType.Number => value.AsNumber.ToString("R", CultureInfo.InvariantCulture),
        CellValueType.Boolean => value.AsBoolean ? "TRUE()" : "FALSE()",
        CellValueType.Text => $"\"{value.AsText.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
        CellValueType.Error => value.AsError.Token,
        _ => string.Empty,
    };

    private static string RenderAddress(CellAddress address) => $"[{SheetDot(address.Sheet)}{Cell(address)}]";

    private static string RenderRange(CellRange range) =>
        $"[{SheetDot(range.Start.Sheet)}{Cell(range.Start)}:.{Cell(range.End)}]";

    private static string Cell(CellAddress a) =>
        $"{(a.ColumnAbsolute ? "$" : string.Empty)}{a.ColumnLabel}{(a.RowAbsolute ? "$" : string.Empty)}{a.RowLabel}";

    private static string SheetDot(string? sheet) => sheet is null ? "." : $"{QuoteSheet(sheet)}.";

    private static string QuoteSheet(string sheet)
    {
        foreach (char c in sheet)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return $"'{sheet.Replace("'", "''", StringComparison.Ordinal)}'";
            }
        }

        return sheet;
    }

    // ---- OpenFormula text → internal A1 ----------------------------------

    private static string StripPrefix(string formula)
    {
        string body = formula.TrimStart();
        if (body.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return body[Prefix.Length..];
        }

        return body.StartsWith('=') ? body[1..] : body;
    }

    private static string ConvertReferencesAndSeparators(string body)
    {
        var sb = new StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            switch (c)
            {
                case '"':
                    i = CopyString(body, i, sb);
                    break;
                case '[':
                    i = ConvertReference(body, i, sb);
                    break;
                case ';':
                    sb.Append(','); // OpenFormula → internal argument separator
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static int CopyString(string body, int start, StringBuilder sb)
    {
        sb.Append('"');
        int i = start + 1;
        while (i < body.Length)
        {
            sb.Append(body[i]);
            if (body[i] == '"')
            {
                if (i + 1 < body.Length && body[i + 1] == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }

                return i;
            }

            i++;
        }

        return i - 1;
    }

    private static int ConvertReference(string body, int start, StringBuilder sb)
    {
        int close = body.IndexOf(']', start);
        if (close < 0)
        {
            sb.Append(body[start]);
            return start;
        }

        string inner = body[(start + 1)..close]; // e.g. ".A1", ".A1:.A3", "Sheet2.A1", "#REF!"
        if (inner is "#REF!" or "#NAME?" or "#N/A" or "#VALUE!" or "#DIV/0!")
        {
            sb.Append("#REF!");
            return close;
        }

        var parts = inner.Split(':');
        for (int p = 0; p < parts.Length; p++)
        {
            if (p > 0)
            {
                sb.Append(':');
            }

            sb.Append(ConvertCorner(parts[p]));
        }

        return close;
    }

    private static string ConvertCorner(string corner)
    {
        // ".A1" → "A1"; "Sheet2.A1" → "Sheet2.A1"; "$Sheet2.A1" → "Sheet2.A1".
        if (corner.StartsWith('.'))
        {
            return corner[1..];
        }

        if (corner.StartsWith('$'))
        {
            corner = corner[1..]; // ODF absolute-sheet marker is dropped (we have no syntax for it)
        }

        // Keep single-quote sheet quoting — internal A1 uses the same form ('My Sheet'.A1).
        return corner;
    }

    // ---- Shared precedence (mirrors FormulaWriter) ------------------------

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
