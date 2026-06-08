namespace Cellestra.Core.Formulas.Functions;

/// <summary>The function call whose parentheses enclose a caret, and which argument the caret is in.</summary>
public sealed record FormulaCallSite(string FunctionName, int ArgumentIndex);

/// <summary>
/// Finds the function call a caret sits inside while a formula is being typed, so the editor can show
/// a positional argument hint (e.g. inside <c>=SUM(A1, </c> the caret is in <c>SUM</c> argument 1).
/// Pure text scanning — no parse tree — so it works on the half-written formulas the AST can't parse.
/// </summary>
public static class FormulaSignatureScanner
{
    /// <summary>
    /// Return the innermost named function call whose parentheses enclose <paramref name="caret"/> in
    /// <paramref name="formula"/> (which may include a leading <c>=</c>), with the zero-based index of
    /// the argument the caret is in. Returns null when the caret isn't inside a function call.
    /// </summary>
    public static FormulaCallSite? Find(string formula, int caret)
    {
        if (string.IsNullOrEmpty(formula))
        {
            return null;
        }

        int end = System.Math.Clamp(caret, 0, formula.Length);
        var stack = new Stack<(int NameStart, int NameEnd, int ArgIndex)>();
        bool inString = false;

        for (int i = 0; i < end; i++)
        {
            char c = formula[i];
            if (inString)
            {
                if (c == '"')
                {
                    if (i + 1 < formula.Length && formula[i + 1] == '"')
                    {
                        i++; // an escaped "" inside a string literal
                    }
                    else
                    {
                        inString = false;
                    }
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(':
                    int j = i - 1;
                    while (j >= 0 && IsNameChar(formula[j]))
                    {
                        j--;
                    }

                    stack.Push((j + 1, i, 0)); // name is formula[j+1 .. i)
                    break;
                case ')':
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    break;
                case ',':
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        stack.Push((top.NameStart, top.NameEnd, top.ArgIndex + 1));
                    }

                    break;
            }
        }

        // Innermost first; skip grouping parens (no name) and report the nearest enclosing named call.
        while (stack.Count > 0)
        {
            var (nameStart, nameEnd, argIndex) = stack.Pop();
            if (nameEnd > nameStart)
            {
                return new FormulaCallSite(formula[nameStart..nameEnd], argIndex);
            }
        }

        return null;
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
