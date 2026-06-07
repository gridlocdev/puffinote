namespace Cellestra.Core.Formulas.Functions;

/// <summary>The category a function belongs to (drives the Formulas-tab menus and the Reference).</summary>
public enum FunctionCategory
{
    Math,
    Statistical,
    Logical,
    Text,
    Lookup,
    DateTime,
}

/// <summary>The data shape a function argument expects (for autocomplete hints / docs).</summary>
public enum ArgumentKind
{
    Any,
    Number,
    Text,
    Boolean,
    Range,
}

/// <summary>One argument in a function's signature.</summary>
public sealed record FunctionArgument(
    string Name,
    ArgumentKind Kind,
    bool Required = true,
    bool Repeatable = false,
    string? Description = null);

/// <summary>
/// Everything three consumers need about a function so they never disagree (ARCHITECTURE §5):
/// the evaluator (arity), autocomplete (hints), and the Function Reference (docs).
/// </summary>
public sealed record FunctionMetadata(
    string Name,
    FunctionCategory Category,
    string Summary,
    IReadOnlyList<FunctionArgument> Arguments,
    ArgumentKind ReturnKind,
    string Example)
{
    /// <summary>Minimum number of arguments required (leading required args, plus none of the optional).</summary>
    public int MinArgs => Arguments.Count(a => a.Required);

    /// <summary>Maximum arguments, or <see cref="int.MaxValue"/> when the last argument repeats.</summary>
    public int MaxArgs => Arguments.Count > 0 && Arguments[^1].Repeatable ? int.MaxValue : Arguments.Count;

    public bool AcceptsArgCount(int count) => count >= MinArgs && count <= MaxArgs;

    /// <summary>A one-line signature like <c>SUM(number1, [number2], …)</c> for hints.</summary>
    public string Signature
    {
        get
        {
            var parts = Arguments.Select(a =>
            {
                string text = a.Repeatable ? $"{a.Name}, …" : a.Name;
                return a.Required ? text : $"[{text}]";
            });
            return $"{Name}({string.Join(", ", parts)})";
        }
    }
}
