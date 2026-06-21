using Cellestra.Core.Formulas.Functions.DateTime;
using Cellestra.Core.Formulas.Functions.Logical;
using Cellestra.Core.Formulas.Functions.Lookup;
using Cellestra.Core.Formulas.Functions.Math;
using Cellestra.Core.Formulas.Functions.Text;

namespace Cellestra.Core.Formulas.Functions;

/// <summary>
/// The one catalog of functions (Golden Rule #5). Evaluation, autocomplete, the function
/// picker, and the Function Reference all read it, so they can never list different sets.
/// Names are matched case-insensitively.
/// </summary>
public sealed class FunctionRegistry
{
    private readonly Dictionary<string, IFunction> _functions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names of functions that recompute every recalculation pass (TODAY, NOW).</summary>
    public static readonly IReadOnlySet<string> VolatileFunctions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TODAY", "NOW" };

    public void Register(IFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        _functions[function.Metadata.Name] = function;
    }

    public bool TryGet(string name, out IFunction function) => _functions.TryGetValue(name, out function!);

    public bool Contains(string name) => _functions.ContainsKey(name);

    public int Count => _functions.Count;

    /// <summary>All function metadata, alphabetical — for autocomplete and the Function Reference.</summary>
    public IEnumerable<FunctionMetadata> AllMetadata =>
        _functions.Values.Select(f => f.Metadata).OrderBy(m => m.Name, StringComparer.Ordinal);

    /// <summary>Build the registry with the complete v1 function library (PRD §8.2).</summary>
    public static FunctionRegistry CreateDefault()
    {
        var registry = new FunctionRegistry();
        registry.RegisterAll(MathFunctions.All());
        registry.RegisterAll(LogicalFunctions.All());
        registry.RegisterAll(TextFunctions.All());
        registry.RegisterAll(LookupFunctions.All());
        registry.RegisterAll(DateTimeFunctions.All());
        return registry;
    }

    private void RegisterAll(IEnumerable<IFunction> functions)
    {
        foreach (var function in functions)
        {
            Register(function);
        }
    }
}
