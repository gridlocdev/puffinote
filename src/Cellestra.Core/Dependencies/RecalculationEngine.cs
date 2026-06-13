using System.Globalization;
using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Core.Dependencies;

/// <summary>The outcome of a recalculation pass.</summary>
public readonly record struct RecalculationResult(int Evaluated, IReadOnlyCollection<CellRef> CircularCells)
{
    public bool HasCircularReference => CircularCells.Count > 0;
}

/// <summary>
/// Recomputes formula cells in dependency order, touching only what an edit affects.
/// Honors the workbook's calculation mode (PRD §8.6), recomputes volatile functions every
/// pass, and reports circular references as #REF! on the offending cells (ARCHITECTURE §5).
/// Pure logic — runs on a background thread at the app layer.
/// </summary>
public sealed class RecalculationEngine
{
    private readonly Workbook _workbook;
    private readonly FunctionRegistry _functions;
    private readonly Func<DateTime> _clock;
    private readonly DependencyGraph _graph = new();
    private readonly Dictionary<CellRef, Expr?> _formulaAst = new();
    private readonly HashSet<CellRef> _volatile = new();

    // Formula cells whose precedents include a range too large to expand into the graph; the range
    // is kept here so an edit to its interior still marks the formula for recalculation.
    private readonly Dictionary<CellRef, List<RangeRef>> _largeRangePrecedents = new();
    private readonly Dictionary<string, Expr?> _parseCache = new(StringComparer.Ordinal);

    public RecalculationEngine(Workbook workbook, FunctionRegistry? functions = null, Func<DateTime>? clock = null)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _functions = functions ?? FunctionRegistry.CreateDefault();
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>The formula cells that contain a volatile function (TODAY/NOW).</summary>
    public IReadOnlyCollection<CellRef> VolatileCells => _volatile;

    /// <summary>Scan the whole workbook, (re)building the dependency graph from every formula cell.</summary>
    public void RebuildGraph()
    {
        _graph.Clear();
        _formulaAst.Clear();
        _volatile.Clear();
        _largeRangePrecedents.Clear();

        foreach (var sheet in _workbook.Worksheets)
        {
            foreach (var (row, column, cell) in sheet.EnumerateCells())
            {
                if (cell.IsFormula)
                {
                    RegisterFormula(new CellRef(sheet.Name, row, column), cell.Formula!);
                }
            }
        }
    }

    /// <summary>Recompute every formula cell in dependency order.</summary>
    public RecalculationResult RecalculateAll() => Evaluate(_formulaAst.Keys.ToList());

    /// <summary>
    /// Update the graph for cells whose content just changed, then recompute them and
    /// everything downstream (plus volatile cells). No-op evaluation when calc mode is Manual,
    /// though the graph is still kept current so a later F9 is correct.
    /// </summary>
    public RecalculationResult RecordEdits(IEnumerable<CellRef> changed)
    {
        var seeds = new HashSet<CellRef>();
        foreach (var cell in changed)
        {
            SyncFormulaState(cell);
            seeds.Add(cell);
        }

        if (_workbook.Settings.CalculationMode == CalculationMode.Manual)
        {
            return new RecalculationResult(0, Array.Empty<CellRef>());
        }

        var affected = CollectAffected(seeds);
        affected.UnionWith(_volatile);
        return Evaluate(affected.ToList());
    }

    /// <summary>Recompute volatile cells and their dependents (e.g. on an F9 in any mode).</summary>
    public RecalculationResult RecalculateVolatile()
    {
        var affected = CollectAffected(_volatile);
        affected.UnionWith(_volatile);
        return Evaluate(affected.ToList());
    }

    // ---- Graph maintenance ------------------------------------------------

    private void SyncFormulaState(CellRef cell)
    {
        var sheet = _workbook.FindSheet(cell.Sheet);
        var model = sheet?.Find(cell.Row, cell.Column);
        if (model is { IsFormula: true })
        {
            RegisterFormula(cell, model.Formula!);
        }
        else
        {
            _graph.RemoveFormula(cell);
            _formulaAst.Remove(cell);
            _volatile.Remove(cell);
            _largeRangePrecedents.Remove(cell);
        }
    }

    private void RegisterFormula(CellRef cell, string formula)
    {
        var ast = ParseCached(formula);
        _formulaAst[cell] = ast;

        if (ast is null)
        {
            _graph.SetPrecedents(cell, Array.Empty<CellRef>());
            _volatile.Remove(cell);
            _largeRangePrecedents.Remove(cell);
            return;
        }

        var precedents = new HashSet<CellRef>();
        var largeRanges = new List<RangeRef>();
        ReferenceCollector.Collect(ast, cell.Sheet, ResolveName, precedents, largeRanges);
        _graph.SetPrecedents(cell, precedents);

        if (largeRanges.Count > 0)
        {
            _largeRangePrecedents[cell] = largeRanges;
        }
        else
        {
            _largeRangePrecedents.Remove(cell);
        }

        if (ReferenceCollector.IsVolatile(ast))
        {
            _volatile.Add(cell);
        }
        else
        {
            _volatile.Remove(cell);
        }
    }

    private Expr? ParseCached(string formula)
    {
        if (_parseCache.TryGetValue(formula, out var cached))
        {
            return cached;
        }

        Expr? ast = Parser.TryParse(formula, out var expr, out _) ? expr : null;
        _parseCache[formula] = ast;
        return ast;
    }

    private CellRange? ResolveName(string name)
    {
        var named = _workbook.FindNamedRange(name);
        return named?.Target;
    }

    // ---- Evaluation -------------------------------------------------------

    private HashSet<CellRef> CollectAffected(IEnumerable<CellRef> seeds)
    {
        var affected = new HashSet<CellRef>();
        var queue = new Queue<CellRef>();
        foreach (var seed in seeds)
        {
            if (_formulaAst.ContainsKey(seed) && affected.Add(seed))
            {
                queue.Enqueue(seed);
            }

            AddDependents(seed, affected, queue);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            AddDependents(current, affected, queue);
        }

        return affected;
    }

    /// <summary>Enqueue every formula that reads <paramref name="cell"/> — via a stored edge, or via
    /// a large precedent range that contains it (those are summarized, not expanded, in the graph).</summary>
    private void AddDependents(CellRef cell, HashSet<CellRef> affected, Queue<CellRef> queue)
    {
        foreach (var dependent in _graph.GetDependents(cell))
        {
            if (affected.Add(dependent))
            {
                queue.Enqueue(dependent);
            }
        }

        if (_largeRangePrecedents.Count == 0)
        {
            return;
        }

        foreach (var (formula, ranges) in _largeRangePrecedents)
        {
            if (!affected.Contains(formula) && ranges.Any(range => range.Contains(cell)) && affected.Add(formula))
            {
                queue.Enqueue(formula);
            }
        }
    }

    private RecalculationResult Evaluate(IReadOnlyCollection<CellRef> nodes)
    {
        if (nodes.Count == 0)
        {
            return new RecalculationResult(0, Array.Empty<CellRef>());
        }

        var nodeSet = nodes as HashSet<CellRef> ?? new HashSet<CellRef>(nodes);
        var ordered = TopologicalSort(nodeSet, out var circular);

        int evaluated = 0;
        foreach (var cell in ordered)
        {
            EvaluateCell(cell);
            evaluated++;
        }

        foreach (var cell in circular)
        {
            WriteResult(cell, CellValue.FromError(CellError.Circular($"Circular reference involving {cell}.")));
        }

        return new RecalculationResult(evaluated, circular);
    }

    private void EvaluateCell(CellRef cell)
    {
        var ast = _formulaAst.TryGetValue(cell, out var stored) ? stored : null;
        if (ast is null)
        {
            WriteResult(cell, CellValue.FromError(CellError.Value("The formula could not be parsed.")));
            return;
        }

        var context = new WorkbookEvaluationContext(_workbook, cell.Sheet, EffectiveCulture(), _clock);
        var evaluator = new Evaluator(context, _functions);
        WriteResult(cell, evaluator.Evaluate(ast));
    }

    private void WriteResult(CellRef cell, CellValue value)
    {
        var sheet = _workbook.FindSheet(cell.Sheet);
        var model = sheet?.Find(cell.Row, cell.Column);
        if (model is { IsFormula: true })
        {
            model.Value = value;
        }
    }

    private CultureInfo EffectiveCulture() => _workbook.Settings.EffectiveLocale;

    /// <summary>Kahn's algorithm over the node set; nodes left in a cycle come back in <paramref name="circular"/>.</summary>
    private List<CellRef> TopologicalSort(HashSet<CellRef> nodes, out HashSet<CellRef> circular)
    {
        var inDegree = new Dictionary<CellRef, int>(nodes.Count);
        foreach (var node in nodes)
        {
            inDegree[node] = 0;
        }

        foreach (var node in nodes)
        {
            foreach (var precedent in _graph.GetPrecedents(node))
            {
                if (nodes.Contains(precedent))
                {
                    inDegree[node]++;
                }
            }
        }

        var ready = new Queue<CellRef>();
        foreach (var (node, degree) in inDegree)
        {
            if (degree == 0)
            {
                ready.Enqueue(node);
            }
        }

        var ordered = new List<CellRef>(nodes.Count);
        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            ordered.Add(node);
            foreach (var dependent in _graph.GetDependents(node))
            {
                if (inDegree.TryGetValue(dependent, out int degree))
                {
                    inDegree[dependent] = --degree;
                    if (degree == 0)
                    {
                        ready.Enqueue(dependent);
                    }
                }
            }
        }

        circular = new HashSet<CellRef>();
        if (ordered.Count != nodes.Count)
        {
            var placed = new HashSet<CellRef>(ordered);
            foreach (var node in nodes)
            {
                if (!placed.Contains(node))
                {
                    circular.Add(node);
                }
            }
        }

        return ordered;
    }
}
