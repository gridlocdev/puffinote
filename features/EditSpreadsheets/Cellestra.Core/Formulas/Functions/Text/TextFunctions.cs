using System.Globalization;
using System.Text;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Functions.Text;

/// <summary>The v1 text functions (PRD §8.2). CONCAT also backs the <c>&amp;</c> operator's library entry.</summary>
public static class TextFunctions
{
    public static IEnumerable<IFunction> All()
    {
        yield return Edge("LEFT", "Returns the first characters of a string.", "LEFT(\"Cellestra\", 4)", fromLeft: true);
        yield return Edge("RIGHT", "Returns the last characters of a string.", "RIGHT(\"Cellestra\", 3)", fromLeft: false);

        yield return new DelegateFunction(
            H.Meta("MID", FunctionCategory.Text, "Returns characters from the middle of a string.", ArgumentKind.Text,
                "MID(\"Cellestra\", 2, 4)", H.Txt("text"), H.Num("start"), H.Num("length")),
            (args, ev) =>
            {
                if (!H.TryText(ev, args[0], out string text, out var et))
                {
                    return et;
                }

                if (!H.TryInt(ev, args[1], out int start, out var es))
                {
                    return es;
                }

                if (!H.TryInt(ev, args[2], out int length, out var el))
                {
                    return el;
                }

                if (start < 1 || length < 0)
                {
                    return CellValue.FromError(CellError.Value("MID start must be ≥ 1 and length ≥ 0."));
                }

                if (start > text.Length)
                {
                    return CellValue.Text(string.Empty);
                }

                int available = System.Math.Min(length, text.Length - (start - 1));
                return CellValue.Text(text.Substring(start - 1, available));
            });

        yield return new DelegateFunction(
            H.Meta("LEN", FunctionCategory.Text, "Returns the number of characters in a string.", ArgumentKind.Number,
                "LEN(\"Cellestra\")", H.Txt("text")),
            (args, ev) => H.TryText(ev, args[0], out string text, out var error)
                ? CellValue.Number(text.Length)
                : error);

        yield return Transform("LOWER", "Converts text to lowercase.", "LOWER(\"ABC\")",
            (text, culture) => text.ToLower(culture));

        yield return Transform("UPPER", "Converts text to uppercase.", "UPPER(\"abc\")",
            (text, culture) => text.ToUpper(culture));

        yield return Transform("TRIM", "Removes extra spaces, leaving single spaces between words.", "TRIM(\"  a  b \")",
            (text, _) => CollapseSpaces(text));

        yield return new DelegateFunction(
            H.Meta("SUBSTITUTE", FunctionCategory.Text, "Replaces occurrences of old text with new text.",
                ArgumentKind.Text, "SUBSTITUTE(\"a-b-c\", \"-\", \"+\")",
                H.Txt("text"), H.Txt("old_text"), H.Txt("new_text"), H.Num("instance", required: false)),
            (args, ev) =>
            {
                if (!H.TryText(ev, args[0], out string text, out var et) ||
                    !H.TryText(ev, args[1], out string oldText, out et) ||
                    !H.TryText(ev, args[2], out string newText, out et))
                {
                    return et;
                }

                if (oldText.Length == 0)
                {
                    return CellValue.Text(text);
                }

                int? instance = null;
                if (args.Count > 3)
                {
                    if (!H.TryInt(ev, args[3], out int parsed, out var ei))
                    {
                        return ei;
                    }

                    if (parsed < 1)
                    {
                        return CellValue.FromError(CellError.Value("SUBSTITUTE instance must be ≥ 1."));
                    }

                    instance = parsed;
                }

                return CellValue.Text(Substitute(text, oldText, newText, instance));
            });

        yield return new DelegateFunction(
            H.Meta("VALUE", FunctionCategory.Text, "Converts text that represents a number into a number.",
                ArgumentKind.Number, "VALUE(\"123\")", H.Txt("text")),
            (args, ev) =>
            {
                if (!H.TryText(ev, args[0], out string text, out var error))
                {
                    return error;
                }

                return H.TryParseNumberOrDate(text, ev.Context.Culture, out double number)
                    ? CellValue.Number(number)
                    : CellValue.FromError(CellError.Value($"'{text}' is not a number."));
            });

        yield return new DelegateFunction(
            H.Meta("TEXT", FunctionCategory.Text, "Formats a number or date as text using a format code.",
                ArgumentKind.Text, "TEXT(0.25, \"0.0%\")", H.Val("value"), H.Txt("format")),
            (args, ev) =>
            {
                var value = ev.Evaluate(args[0]);
                if (value.IsError)
                {
                    return value;
                }

                if (!H.TryText(ev, args[1], out string format, out var ef))
                {
                    return ef;
                }

                return CellValue.Text(FormatValue(value, format, ev.Context.Culture));
            });

        yield return new DelegateFunction(
            H.Meta("CONCAT", FunctionCategory.Text, "Joins several text values into one.", ArgumentKind.Text,
                "CONCAT(A1, \" \", B1)", H.Val("text", repeatable: true)),
            (args, ev) =>
            {
                var sb = new StringBuilder();
                foreach (var arg in args)
                {
                    foreach (var value in ev.EnumerateValues(arg))
                    {
                        if (value.IsError)
                        {
                            return value;
                        }

                        if (Coercion.TryToText(value, out string text, out _))
                        {
                            sb.Append(text);
                        }
                    }
                }

                return CellValue.Text(sb.ToString());
            });
    }

    private static IFunction Edge(string name, string summary, string example, bool fromLeft) =>
        new DelegateFunction(
            H.Meta(name, FunctionCategory.Text, summary, ArgumentKind.Text, example,
                H.Txt("text"), H.Num("count", required: false)),
            (args, ev) =>
            {
                if (!H.TryText(ev, args[0], out string text, out var et))
                {
                    return et;
                }

                int count = 1;
                if (args.Count > 1 && !H.TryInt(ev, args[1], out count, out var ec))
                {
                    return ec;
                }

                if (count < 0)
                {
                    return CellValue.FromError(CellError.Value($"{name} count must be ≥ 0."));
                }

                count = System.Math.Min(count, text.Length);
                return CellValue.Text(fromLeft ? text[..count] : text[(text.Length - count)..]);
            });

    private static IFunction Transform(string name, string summary, string example, Func<string, CultureInfo, string> op) =>
        new DelegateFunction(
            H.Meta(name, FunctionCategory.Text, summary, ArgumentKind.Text, example, H.Txt("text")),
            (args, ev) => H.TryText(ev, args[0], out string text, out var error)
                ? CellValue.Text(op(text, ev.Context.Culture))
                : error);

    private static string CollapseSpaces(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool previousSpace = false;
        foreach (char c in text.Trim())
        {
            if (c == ' ')
            {
                if (!previousSpace)
                {
                    sb.Append(' ');
                }

                previousSpace = true;
            }
            else
            {
                sb.Append(c);
                previousSpace = false;
            }
        }

        return sb.ToString();
    }

    private static string Substitute(string text, string oldText, string newText, int? instance)
    {
        if (instance is null)
        {
            return text.Replace(oldText, newText, StringComparison.Ordinal);
        }

        int occurrence = 0;
        int index = 0;
        while ((index = text.IndexOf(oldText, index, StringComparison.Ordinal)) >= 0)
        {
            occurrence++;
            if (occurrence == instance)
            {
                return string.Concat(text.AsSpan(0, index), newText, text.AsSpan(index + oldText.Length));
            }

            index += oldText.Length;
        }

        return text;
    }

    private static string FormatValue(CellValue value, string format, CultureInfo culture) =>
        // One formatting path shared with the grid and status bar.
        Styling.NumberFormatter.Format(value, format, culture);
}
